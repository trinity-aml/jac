﻿using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Mvc;
using JacRed.Engine.CORE;
using JacRed.Engine.Parse;
using JacRed.Models.tParse;
using JacRed.Engine;
using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;

namespace JacRed.Controllers
{
    [Route("bitru/[action]")]
    public class BitruController : BaseController
    {
        #region parseMagnet
        async public Task<ActionResult> parseMagnet(int id)
        {
            string key = $"bitru:parseMagnet:{id}";
            if (Startup.memoryCache.TryGetValue(key, out byte[] _m))
                return File(_m, "application/x-bittorrent");

            byte[] _t = await HttpClient.Download($"{AppInit.conf.Bitru.host}/download.php?id={id}", referer: $"{AppInit.conf.Bitru}/details.php?id={id}", useproxy: AppInit.conf.Bitru.useproxy, timeoutSeconds: 10);
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
            if (!AppInit.conf.Bitru.enable)
                return false;

            #region Кеш
            string cachekey = $"kinozal:{string.Join(":", cats ?? new string[] { })}:{query}";
            if (!HtmlCache.Read(cachekey, out string cachehtml))
            {
                string html = await HttpClient.Get($"{AppInit.conf.Bitru.host}/browse.php?s={HttpUtility.HtmlEncode(query)}&sort=&tmp=&cat=&subcat=&year=&country=&sound=&soundtrack=&subtitles=#content", useproxy: AppInit.conf.Bitru.useproxy, timeoutSeconds: AppInit.conf.timeoutSeconds);

                if (html != null)
                {
                    cachehtml = html;
                    HtmlCache.Write(cachekey, html);
                }

                if (cachehtml == null)
                    return false;
            }
            #endregion

            foreach (string row in tParse.ReplaceBadNames(cachehtml).Split("<div class=\"b-title\"").Skip(1))
            {
                if (row.Contains(">Аниме</a>") || row.Contains(">Мульт"))
                    continue;

                #region Локальный метод - Match
                string Match(string pattern, int index = 1)
                {
                    string res = HttpUtility.HtmlDecode(new Regex(pattern, RegexOptions.IgnoreCase).Match(row).Groups[index].Value.Trim());
                    res = Regex.Replace(res, "[\n\r\t ]+", " ");
                    return res.Trim();
                }
                #endregion

                if (string.IsNullOrWhiteSpace(row))
                    continue;

                #region Дата создания
                DateTime createTime = default;

                if (row.Contains("<span>Сегодня"))
                {
                    createTime = DateTime.Today;
                }
                else if (row.Contains("<span>Вчера"))
                {
                    createTime = DateTime.Today.AddDays(-1);
                }
                else
                {
                    createTime = tParse.ParseCreateTime(Match("<div class=\"ellips\"><span>([0-9]{2} [^ ]+ [0-9]{4}) в [0-9]{2}:[0-9]{2} от <a"), "dd.MM.yyyy");
                }

                //if (createTime == default)
                //    continue;
                #endregion

                #region Данные раздачи
                string url = Match("href=\"(details.php\\?id=[0-9]+)\"");
                string newsid = Match("href=\"details.php\\?id=([0-9]+)\"");
                string cat = Match("<a href=\"browse.php\\?tmp=(movie|serial)&");

                string title = Match("<div class=\"it-title\">([^<]+)</div>");
                string _sid = Match("<span class=\"b-seeders\">([0-9]+)</span>");
                string _pir = Match("<span class=\"b-leechers\">([0-9]+)</span>");
                string sizeName = Match("title=\"Размер\">([^<]+)</td>");

                if (string.IsNullOrWhiteSpace(cat) || string.IsNullOrWhiteSpace(newsid) || string.IsNullOrWhiteSpace(title))
                    continue;

                url = $"{AppInit.conf.Bitru.host}/{url}";
                #endregion

                #region Парсим раздачи
                int relased = 0;
                string name = null, originalname = null;

                if (cat == "movie")
                {
                    #region Фильмы
                    // Звонок из прошлого / Звонок / Kol / The Call (2020)
                    var g = Regex.Match(title, "^([^/\\(]+) / [^/]+ / [^/]+ / ([^/\\(]+) \\(([0-9]{4})\\)").Groups;
                    if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                    {
                        name = g[1].Value;
                        originalname = g[2].Value;

                        if (int.TryParse(g[3].Value, out int _yer))
                            relased = _yer;
                    }
                    else
                    {
                        // Код бессмертия / Код молодости / Eternal Code (2019)
                        g = Regex.Match(title, "^([^/\\(]+) / [^/]+ / ([^/\\(]+) \\(([0-9]{4})\\)").Groups;
                        if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                        {
                            name = g[1].Value;
                            originalname = g[2].Value;

                            if (int.TryParse(g[3].Value, out int _yer))
                                relased = _yer;
                        }
                        else
                        {
                            // Брешь / Breach (2020)
                            g = Regex.Match(title, "^([^/\\(]+) / ([^/\\(]+) \\(([0-9]{4})\\)").Groups;
                            if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                            {
                                name = g[1].Value;
                                originalname = g[2].Value;

                                if (int.TryParse(g[3].Value, out int _yer))
                                    relased = _yer;
                            }
                            else
                            {
                                // Жертва (2020)
                                g = Regex.Match(title, "^([^/\\(]+) \\(([0-9]{4})\\)").Groups;

                                name = g[1].Value;
                                if (int.TryParse(g[2].Value, out int _yer))
                                    relased = _yer;
                            }
                        }
                    }
                    #endregion
                }
                else if (cat == "serial")
                {
                    #region Сериалы
                    if (row.Contains("сезон"))
                    {
                        // Золотое Божество 3 сезон (1-12 из 12) / Gōruden Kamui / Golden Kamuy (2020)
                        var g = Regex.Match(title, "^([^/\\(]+) [0-9\\-]+ сезон [^/]+ / [^/]+ / ([^/\\(]+) \\(([0-9]{4})(\\)|-)").Groups;
                        if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                        {
                            name = g[1].Value;
                            originalname = g[2].Value;

                            if (int.TryParse(g[3].Value, out int _yer))
                                relased = _yer;
                        }
                        else
                        {
                            // Ход королевы / Ферзевый гамбит 1 сезон (1-7 из 7) / The Queen's Gambit (2020)
                            g = Regex.Match(title, "^([^/\\(]+) / [^/]+ / ([^/\\(]+) \\(([0-9]{4})(\\)|-)").Groups;
                            if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                            {
                                name = g[1].Value;
                                originalname = g[2].Value;

                                if (int.TryParse(g[3].Value, out int _yer))
                                    relased = _yer;
                            }
                            else
                            {
                                // Доллар 1 сезон (1-15 из 15) / Dollar (2019)
                                // Эш против Зловещих мертвецов 1-3 сезон (1-30 из 30) / Ash vs Evil Dead (2015-2018)
                                g = Regex.Match(title, "^([^/\\(]+) [0-9\\-]+ сезон [^/]+ / ([^/\\(]+) \\(([0-9]{4})(\\)|-)").Groups;
                                if (!string.IsNullOrWhiteSpace(g[1].Value) && !string.IsNullOrWhiteSpace(g[2].Value) && !string.IsNullOrWhiteSpace(g[3].Value))
                                {
                                    name = g[1].Value;
                                    originalname = g[2].Value;

                                    if (int.TryParse(g[3].Value, out int _yer))
                                        relased = _yer;
                                }
                                else
                                {
                                    // СашаТаня 6 сезон (1-19 из 22) (2021)
                                    // Метод 1-2 сезон (1-26 из 32) (2015-2020)
                                    g = Regex.Match(title, "^([^/\\(]+) [0-9\\-]+ сезон \\([^\\)]+\\) +\\(([0-9]{4})(\\)|-)").Groups;

                                    name = g[1].Value;
                                    if (int.TryParse(g[2].Value, out int _yer))
                                        relased = _yer;
                                }
                            }
                        }
                    }
                    else
                    {
                        // Проспект обороны (1-16 из 16) (2019)
                        var g = Regex.Match(title, "^([^/\\(]+) \\([^\\)]+\\) +\\(([0-9]{4})(\\)|-)").Groups;

                        name = g[1].Value;
                        if (int.TryParse(g[2].Value, out int _yer))
                            relased = _yer;
                    }
                    #endregion
                }
                #endregion

                if (!string.IsNullOrWhiteSpace(name) || cats == null)
                {
                    #region types
                    string[] types = null;
                    switch (cat)
                    {
                        case "movie":
                            types = new string[] { "movie" };
                            break;
                        case "serial":
                            types = new string[] { "serial" };
                            break;
                    }

                    if (types == null)
                        continue;

                    if (cats != null)
                    {
                        bool isok = false;
                        foreach (string c in cats)
                        {
                            if (types.Contains(c))
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
                        trackerName = "bitru",
                        types = types,
                        url = url,
                        title = title,
                        sid = sid,
                        pir = pir,
                        sizeName = sizeName,
                        createTime = createTime,
                        parselink = $"{host}/bitru/parsemagnet?id={newsid}",
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
