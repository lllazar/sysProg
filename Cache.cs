using System.Collections.Concurrent;

namespace sysProg
{
    public class Cache // long za id jer sta ako Rijaks i (konceptulano) dodje do koriscenja duzih idjeva
    {
        private readonly int maxSize;
        private readonly ConcurrentDictionary<long, string> cache; //samo po sebi jeste safe, ne bi morao lock za operacije nad strukturom, medjutim pridruzuje se lancana lista za LRU oslobadjanje kesa
        private readonly LinkedList<long> keys;//samim tim posto ce sve operacije da budu u locku, optimalnije je koristiti Dictionary, ukoliko se promeni nacin oslobadjanja kesa moglo bi biti korisno da recnik bude thread - safe
        private readonly object lockObject;
        private readonly Logger logger;

        public Cache(int maxSize = 100, Logger logger=null)
        {
            this.maxSize = maxSize;
            this.cache = new ConcurrentDictionary<long, string>();
            this.keys = new LinkedList<long>();
            this.lockObject = new object();
            this.logger = logger ?? new Logger();
        }

        public void Add(long key, string value) //sve metode rade touch, refresuju kes podatak
        {
            
            lock (lockObject)
            {
                if (cache.TryGetValue(key, out _))
                { 
                    logger.Log("Cache hit!");
                    Touch(key);

                    return;
                }
                if (keys.Count >= maxSize)
                {
                    long oldKey = keys.First.Value;
                    logger.Log("Cache is full! Removing LRU data");
                    if (cache.TryRemove(oldKey, out _))
                    {
                        keys.RemoveFirst();
                    }
                    else
                    {
                        logger.Log("Problem with removing cache!");
                        return;
                    }
                    
                    
                }
                if(cache.TryAdd(key, value))
                {
                    keys.AddLast(key);
                }
                else
                {
                    logger.Log("Problem with adding cache!");
                    return;
                }

                logger.Log($"id({key}) which is mapped to {value} added to cache\n");
            }
        }

        public string Get(long key)
        {
            lock (lockObject)
            {
                if (cache.TryGetValue(key, out string value))
                {
                    Touch(key);

                    logger.Log("Cache hit");

                    return value;
                }
                else
                {
                    return string.Empty;
                }
            }
        }

        private void Touch(long key)
        {
            keys.Remove(key);
            keys.AddLast(key);
        }

        public void ClearCache(int percentage) // metoda koja ne nalazi toliku primenu ovde, ali daje fleksibilnost - oslobadjanje tacno odrednjenog procenata kesa.
        {
            if (percentage <= 0 || percentage > 100)
            {
                logger.Log(($"You can not clear {percentage}% of cache"));
                return;
            }
            lock (lockObject)
            {
                int factor = (int)(percentage / 100.0 * cache.Count);

                for (int i = 0; i < factor; i++)
                {
                    long key = keys.First.Value;
                    keys.RemoveFirst();
                    cache.TryRemove(key, out _);
                }
            }
        }
    }
}
