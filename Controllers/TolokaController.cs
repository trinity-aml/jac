﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using JacRed.Engine.CORE;
using JacRed.Engine.Parse;
using JacRed.Models.tParse;
using JacRed.Engine;
using System.Collections.Concurrent;

namespace JacRed.Controllers
{
    [Route("toloka/[action]")]
    public class TolokaController : BaseController
    {
        #region Cookie / TakeLogin
        static string Cookie(IMemoryCache memoryCache)
        {
            if (memoryCache.TryGetValue("cron:TolokaController:Cookie", out string cookie))
                return cookie;

            return null;
        }

        async static Task<bool> TakeLogin(IMemoryCache memoryCache)
        {
            try
            {
                var clientHandler = new System.Net.Http.HttpClientHandler()
                {
                    AllowAutoRedirect = false
                };

                clientHandler.ServerCertificateCustomValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;
                using (var client = new System.Net.Http.HttpClient(clientHandler))
                {
                    client.Timeout = TimeSpan.FromSeconds(AppInit.conf.timeoutSeconds);
                    client.MaxResponseContentBufferSize = 2000000; // 2MB
                    client.DefaultRequestHeaders.Add("user-agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/75.0.3770.100 Safari/537.36");

                    var postParams = new Dictionary<string, string>();
                    postParams.Add("username", AppInit.conf.Toloka.login.u);
                    postParams.Add("password", AppInit.conf.Toloka.login.p);
                    postParams.Add("autologin", "on");
                    postParams.Add("ssl", "on");
                    postParams.Add("redirect", "index.php?");
                    postParams.Add("login", "Вхід");

                    using (var postContent = new System.Net.Http.FormUrlEncodedContent(postParams))
                    {
                        using (var response = await client.PostAsync($"{AppInit.conf.Toloka.host}/login.php", postContent))
                        {
                            if (response.Headers.TryGetValues("Set-Cookie", out var cook))
                            {
                                string toloka_sid = null, toloka_data = null;
                                foreach (string line in cook)
                                {
                                    if (string.IsNullOrWhiteSpace(line))
                                        continue;

                                    if (line.Contains("toloka_sid="))
                                        toloka_sid = new Regex("toloka_sid=([^;]+)(;|$)").Match(line).Groups[1].Value;

                                    if (line.Contains("toloka_data="))
                                        toloka_data = new Regex("toloka_data=([^;]+)(;|$)").Match(line).Groups[1].Value;
                                }

                                if (!string.IsNullOrWhiteSpace(toloka_sid) && !string.IsNullOrWhiteSpace(toloka_data))
                                {
                                    memoryCache.Set("cron:TolokaController:Cookie", $"toloka_sid={toloka_sid}; toloka_ssl=1; toloka_data={toloka_data};", TimeSpan.FromHours(1));
                                    return true;
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            return false;
        }
        #endregion

        #region parseMagnet
        async public Task<ActionResult> parseMagnet(int id)
        {
            string key = $"toloka:parseMagnet:{id}";
            if (Startup.memoryCache.TryGetValue(key, out byte[] _m))
                return File(_m, "application/x-bittorrent");

            #region Авторизация
            if (Cookie(memoryCache) == null)
            {
                string authKey = "toloka:TakeLogin()";
                if (memoryCache.TryGetValue(authKey, out _))
                    return Content("TakeLogin == false");

                if (await TakeLogin(memoryCache) == false)
                {
                    memoryCache.Set(authKey, 0, TimeSpan.FromMinutes(1));
                    return Content("TakeLogin == false");
                }
            }
            #endregion

            byte[] _t = await HttpClient.Download($"{AppInit.conf.Toloka.host}/download.php?id={id}", cookie: Cookie(memoryCache), referer: AppInit.conf.Toloka.host, timeoutSeconds: 10);
            if (_t != null)
            {
                Startup.memoryCache.Set(key, _t, DateTime.Now.AddMinutes(AppInit.conf.magnetCacheToMinutes));
                return File(_t, "application/x-bittorrent");
            }

            return Content("error");
        }
        #endregion

        #region parsePage
        async public static Task<bool> parsePage(string host, ConcurrentBag<TorrentDetails> torrents, string query, string[] cats)
        {
            if (!AppInit.conf.Toloka.enable)
                return false;

            #region Авторизация
            if (Cookie(Startup.memoryCache) == null)
            {
                string authKey = "toloka:TakeLogin()";
                if (Startup.memoryCache.TryGetValue(authKey, out _))
                    return false;

                if (await TakeLogin(Startup.memoryCache) == false)
                {
                    Startup.memoryCache.Set(authKey, 0, TimeSpan.FromMinutes(1));
                    return false;
                }
            }
            #endregion

            #region Кеш
            string cachekey = $"toloka:{string.Join(":", cats ?? new string[] { })}:{query}";
            if (!HtmlCache.Read(cachekey, out string cachehtml))
            {
                string html = await HttpClient.Get($"{AppInit.conf.Toloka.host}/tracker.php?prev_sd=0&prev_a=0&prev_my=0&prev_n=0&prev_shc=0&prev_shf=1&prev_sha=1&prev_cg=0&prev_ct=0&prev_at=0&prev_nt=0&prev_de=0&prev_nd=0&prev_tcs=1&prev_shs=0&f%5B%5D=-1&o=1&s=2&tm=-1&shf=1&sha=1&tcs=1&sns=-1&sds=-1&nm={HttpUtility.UrlEncode(query)}&pn=&send=%D0%9F%D0%BE%D1%88%D1%83%D0%BA", cookie: Cookie(Startup.memoryCache), /*useproxy: AppInit.conf.useproxyToloka,*/ timeoutSeconds: AppInit.conf.timeoutSeconds);

                if (html != null && html.Contains("<html lang=\"uk\""))
                {
                    cachehtml = html;
                    HtmlCache.Write(cachekey, html);
                }

                if (cachehtml == null)
                    return false;
            }
            #endregion

            foreach (string row in tParse.ReplaceBadNames(cachehtml).Split("</tr>"))
            {
                if (string.IsNullOrWhiteSpace(row) || Regex.IsMatch(row, "Збір коштів", RegexOptions.IgnoreCase))
                    continue;

                #region Локальный метод - Match
                string Match(string pattern, int index = 1)
                {
                    string res = HttpUtility.HtmlDecode(new Regex(pattern, RegexOptions.IgnoreCase).Match(row).Groups[index].Value.Trim());
                    res = Regex.Replace(res, "[\n\r\t ]+", " ");
                    return res.Trim();
                }
                #endregion

                #region Дата создания
                string _createTime = Match("class=\"gensmall\">([0-9]{4}-[0-9]{2}-[0-9]{2})").Replace("-", ".");
                DateTime.TryParse(_createTime, out DateTime createTime);

                //if (!DateTime.TryParse(_createTime, out DateTime createTime) || createTime == default)
                //    continue;
                #endregion

                #region Данные раздачи
                string url = Match("class=\"topictitle genmed\"><a class=\"[^\"]+\" href=\"(t[0-9]+)\"");
                string title = Match("class=\"topictitle genmed\"><a [^>]+><b>([^<]+)</b></a>");
                string downloadid = Match("href=\"download.php\\?id=([0-9]+)\"");
                string tracker = Match("class=\"gen\" href=\"tracker.php\\?f=([0-9]+)");
                string _sid = Match("class=\"seedmed\"><b>([0-9]+)</b>");
                string _pir = Match("class=\"leechmed\"><b>([0-9]+)</b>");
                string sizeName = Match("class=\"gensmall\">([0-9\\.]+ (MB|GB))</td>");

                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(downloadid) || string.IsNullOrWhiteSpace(tracker) || sizeName == "0 B")
                    continue;

                url = $"{AppInit.conf.Toloka.host}/{url}";
                #endregion

                #region Парсим раздачи
                int relased = 0;
                string name = null, originalname = null;

                if (tracker is "16" or "96" or "19" or "139")
                {
                    #region Фильмы
                    // Незворотність / Irréversible / Irreversible (2002) AVC Ukr/Fre | Sub Eng
                    var g = Regex.Match(title, "^([^/\\(\\[]+) / [^/\\(\\[]+ / ([^/\\(\\[]+) \\(([0-9]{4})(\\)|-)").Groups;
                    if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                    {
                        name = g[1].Value;
                        originalname = g[2].Value;

                        if (int.TryParse(g[3].Value, out int _yer))
                            relased = _yer;
                    }
                    else
                    {
                        // Мій рік у Нью-Йорку / My Salinger Year (2020) Ukr/Eng
                        g = Regex.Match(title, "^([^/\\(\\[]+) / ([^/\\(\\[]+) \\(([0-9]{4})(\\)|-)").Groups;
                        if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                        {
                            name = g[1].Value;
                            originalname = g[2].Value;

                            if (int.TryParse(g[3].Value, out int _yer))
                                relased = _yer;
                        }
                    }
                    #endregion
                }
                else if (tracker is "32" or "173" or "174" or "44" or "230" or "226" or "227" or "228" or "229")
                {
                    #region Сериалы
                    // Дім з прислугою (Сезон 2, серії 1-8) / Servant (Season 2, episodes 1-8) (2021) WEB-DLRip-AVC Ukr/Eng
                    var g = Regex.Match(title, "^([^/\\(\\[]+) (\\([^\\)]+\\) )?/ ([^/\\(\\[]+) (\\([^\\)]+\\) )?\\(([0-9]{4})(\\)|-)").Groups;
                    if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[3].Value) && !string.IsNullOrWhiteSpace(g[5].Value))
                    {
                        name = g[1].Value;
                        originalname = g[3].Value;

                        if (int.TryParse(g[5].Value, out int _yer))
                            relased = _yer;
                    }
                    #endregion
                }
                #endregion

                if (!string.IsNullOrWhiteSpace(name) || cats == null)
                {
                    #region types
                    string[] types = null;
                    switch (tracker)
                    {
                        case "16":
                        case "96":
                            types = new string[] { "movie" };
                            break;
                        case "19":
                        case "139":
                            types = new string[] { "multfilm" };
                            break;
                        case "32":
                        case "173":
                            types = new string[] { "serial" };
                            break;
                        case "174":
                        case "44":
                            types = new string[] { "multserial" };
                            break;
                        case "226":
                        case "227":
                        case "228":
                        case "229":
                        case "230":
                            types = new string[] { "docuserial", "documovie" };
                            break;
                    }

                    if (types == null)
                        continue;

                    if (cats != null)
                    {
                        bool isok = false;
                        foreach (string cat in cats)
                        {
                            if (types.Contains(cat))
                                isok = true;
                        }

                        if (!isok)
                            continue;
                    }
                    #endregion

                    int.TryParse(_sid, out int sid);
                    int.TryParse(_pir, out int pir);

                    torrents.Add(new TorrentDetails()
                    {
                        trackerName = "toloka",
                        types = types,
                        url = url,
                        title = title,
                        sid = sid,
                        pir = pir,
                        sizeName = sizeName,
                        createTime = createTime,
                        parselink = $"{host}/toloka/parsemagnet?id={downloadid}",
                        name = name,
                        originalname = originalname,
                        relased = relased
                    });
                }
            }

            return true;
        }
        #endregion
    }
}
