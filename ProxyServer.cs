using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Text.Json;

namespace sysProg
{
    public class ProxyServer
    {
        private readonly Cache cache;
        private readonly Logger logger;
        private static readonly HttpClient client = new HttpClient();
        private static readonly ConcurrentDictionary<long, object> locks = new ConcurrentDictionary<long, object>();

        public ProxyServer(int cacheSize = 100, Logger logger = null)
        {
            this.cache = new Cache(cacheSize, logger);
            this.logger = logger ?? new Logger();
        }

        public List<long> Request(string param) // sta kod da krene naopako u svakom slucaju se vraca prazna lista
        {
            if (string.IsNullOrWhiteSpace(param))
            {
                return new List<long>();
            }
     
            string url = $"https://data.rijksmuseum.nl/search/collection{param}";
            try
            {
                HttpResponseMessage resp = client.GetAsync(url).Result;

                resp.EnsureSuccessStatusCode();
                logger.Log("Response from Rijks was OK");

                string content = resp.Content.ReadAsStringAsync().Result;

                if (string.IsNullOrWhiteSpace(content))
                {
                    logger.Log("Empty response body from API");
                    return new List<long>();
                }

                using JsonDocument doc = JsonDocument.Parse(content);


                JsonElement root = doc.RootElement;
                JsonElement items = root.GetProperty("orderedItems");

                List<long> ids = new List<long>();

                foreach (JsonElement element in items.EnumerateArray())
                {
                    string link = element.GetProperty("id").GetString() ?? "";
                    string lastPart = link.Split("/").Last();

                    if (long.TryParse(lastPart, out long id))
                    {
                        ids.Add(id);
                    }
                    else
                    {
                        logger.Log($"Could not extract id from {link}");
                    }
                }
                return ids;
            }
            catch (Exception ex)
            {
                logger.Log($"ERROR! in method Request(string {param}): " + ex.ToString());
                return new List<long>();
            }

        }

        public string getPicURL(long id)
        {
            string picURL = cache.Get(id);

            if (!string.IsNullOrEmpty(picURL))
                return picURL;

            object locker = locks.GetOrAdd(id, _ => new object()); // per-key lock, zadrzavanaje konkuretnosti

            lock (locker)
            {
                try
                {
                    picURL = cache.Get(id);
                    if (!string.IsNullOrEmpty(picURL))
                    {
                        return picURL;
                    }

                    string searchURL = "https://data.rijksmuseum.nl/" + id.ToString();

                    HttpResponseMessage resp = client.GetAsync(searchURL).Result;

                    picURL = resp.RequestMessage?.RequestUri?.ToString() ?? string.Empty;

                    if (resp.IsSuccessStatusCode)
                    {
                        cache.Add(id, picURL);
                        return picURL;
                    }

                    logger.Log($"For id:{id} no link could be resloved");
                    

                    return string.Empty;
                }
                finally
                {
                    locks.TryRemove(id, out _);
                }
               
                
            }
        }

        public void ClearServer()
        {
            ThreadPool.GetMaxThreads(out int max, out _);
            ThreadPool.GetAvailableThreads(out int free, out _);

            if (max - free < 4) // ovo je samo radi testiranja, kod kompleksnih sistema ovde bi se pozvala neka funckija ili event koji bi signalizirao da je server idle. 3 ce uvek biti aktivne(vidi se na osnovu maina) i to predstavlja idle za konkretan primer
            {
                cache.ClearCache(50);// moguce je koristiti neku funkciju koja vraca broj koji predstavlja procenat koliko osloboditi kes- memorije na osnovu stanja okruzenja. Ovde je uzeto najprostiji slucaj: prepoloviti kes svaki put kada se pozove metoda
                logger.Log("Server is idle. Cleared 50% of cache.");
            }
        }
    }
}
