using System.Net;
using System.Text;

namespace sysProg
{

    public class Program
    {
        static volatile bool end = false;
        static Queue<HttpListenerContext> queue = new Queue<HttpListenerContext>();
        static object lockObj = new object();
        static Logger logger = new Logger();
        static ProxyServer server = new ProxyServer(500,logger);
        static SemaphoreSlim semaphore = new SemaphoreSlim(10); 



        static void Main()
        {
            ThreadPool.SetMaxThreads(20, 20);
            logger.ChangeLogFile("log.txt");
            HttpListener listener = new HttpListener();
            listener.Prefixes.Add("http://localhost:5000/");
            listener.Start();

            ThreadPool.QueueUserWorkItem(_ => ShutdownThread());
            ThreadPool.QueueUserWorkItem(_ => ClearingThread());

            while (!end)
            {
                HttpListenerContext context = listener.GetContext();
                lock (lockObj)
                {
                    queue.Enqueue(context);
                    Monitor.Pulse(lockObj);
                }
                ThreadPool.QueueUserWorkItem(_ => ProcessingThread());
            }
        }

        static void ProcessingThread()
        {
            try
            {
                HttpListenerContext context;
                lock (lockObj)
                {
                    while (!end && queue.Count == 0)
                    {
                        Monitor.Wait(lockObj);

                    }
                    if (end && queue.Count == 0)
                    {
                        return;
                    }
                    context = queue.Dequeue();
                }
              

                HttpListenerRequest request = context.Request;
                HttpListenerResponse response = context.Response;
                string param = request.Url.Query;

                string responseText;

                List<long> ids = server.Request(param);
                if (ids.Count == 0)
                {
                    responseText = "<li>No results found</li>";
                    logger.Log("Empty list returned");
                }
                else
                {
                    List<string> urls = new List<string>();
                    List<Thread> threads = new List<Thread>();

                    foreach (long id in ids)
                    {
                        long localId=id;
                        semaphore.Wait();
                        Thread t = new Thread(() =>
                        {
                            try
                            {
                                string url = server.getPicURL(localId);
                                if (!string.IsNullOrEmpty(url))
                                {
                                    lock (lockObj)
                                    {
                                        urls.Add(url);
                                    }
                                }
                            }
                            finally
                            {
                                semaphore.Release();
                            }
                            
                        });
                        threads.Add(t);
                        t.Start();
                    }

                    foreach (Thread t in threads)
                        t.Join();

                    responseText = string.Join("\n", urls.Select(u => $"<li><a href='{u}'>{u}</a></li>"));
                }
                
                byte[] buffer = Encoding.UTF8.GetBytes($"<html><body><ul>{responseText}</ul></body></html>");

                response.ContentType = "text/html; charset=utf-8";
                response.ContentLength64 = buffer.Length;
                response.OutputStream.Write(buffer, 0, buffer.Length);
                response.OutputStream.Close();
            }
            catch (Exception ex)
            {
                logger.Log($"ERROR in processing thread: {ex}");
            }

        }

        static void ShutdownThread()
        {
           
            Console.WriteLine("Press 67 for shutdown\n");
            
            while (true)
            {
                string input = Console.ReadLine();
                if (input == "67")
                {
                    end = true;
                    logger.Log("Shutting down server");
                    logger.Write();
                    logger.CloseStream(); 
                    Environment.Exit(0);
                    break;
                }
            }
        }

        static void ClearingThread()
        {
            while (!end)
            {
                Thread.Sleep(10000);
                server.ClearServer();
            }
        }
    }
}
