using JacRed.Models;
using Newtonsoft.Json;
using System.IO;

namespace JacRed
{
    public class AppInit
    {
        public static AppInit conf = JsonConvert.DeserializeObject<AppInit>(File.ReadAllText("init.conf"));


        public int timeoutSeconds = 5;

        public int htmlCacheToMinutes = 1;

        public int magnetCacheToMinutes = 2;

        public string apikey = null;

        public TrackerSettings Rutor = new TrackerSettings("http://rutor.info", true, false);

        public TrackerSettings TorrentBy = new TrackerSettings("http://torrent.by", true, false);

        public TrackerSettings Kinozal = new TrackerSettings("http://kinozal.tv", true, false);

        public TrackerSettings NNMClub = new TrackerSettings("https://nnmclub.to", true, false);

        public TrackerSettings Bitru = new TrackerSettings("https://bitru.org", true, false);

        public TrackerSettings Toloka = new TrackerSettings("https://toloka.to", false, false, null);

        public TrackerSettings Rutracker = new TrackerSettings("https://rutracker.net", false, false, null);

        public TrackerSettings Underverse = new TrackerSettings("https://underver.se", false, false, null);

        public ProxySettings proxy = new ProxySettings();
    }
}
