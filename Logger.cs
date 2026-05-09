using System.Collections.Concurrent;
//logger je zamisljen da se podrazumeva da loguje na konzolu, a u svakom trenutku je moguce porminiti stream na koji logguje
//vracanje na konzolu je moguce unosenjem "console" kao ime fajla
namespace sysProg
{
    public class Logger
    {
        private readonly int limit;
        private readonly ConcurrentQueue<string> buff;        
        private StreamWriter fs;
        private bool isFsConsole; 
        readonly object locker;

        public Logger(int limit = 10)
        {
            this.limit = limit;
            this.buff = new ConcurrentQueue<string>();     //thread safe, ali stream writer nije, zato svaka operacija nad njim ide u lock
            this.fs = new StreamWriter(Console.OpenStandardOutput());
            this.isFsConsole = true;
            this.locker = new object();
        }

        public void ChangeLogFile(string fileName)
        {
            lock (locker)
            {
                Write();
                if (fileName == "console")
                {
                    if (isFsConsole)
                    {
                        return;
                    }
                    fs.Close();
                    isFsConsole = true;
                    fs = new StreamWriter(Console.OpenStandardOutput());
                }
                else
                {
                    if(!isFsConsole)
                    {
                        fs.Close();
                    }                    
                    isFsConsole = false;
                    fs = new StreamWriter(fileName, append: false);
                }
            }
        }
        public void CloseStream()
        {
            lock (locker)
            {
                fs.Flush();
                if (!isFsConsole)
                {
                    fs.Close();
                }                              
            }          
        }

        public void Log(string msg) // nema locka zato sto je write u locku, ince bi moglo da dodje do double lock
        {
            buff.Enqueue(msg);

            if (buff.Count >= limit)
            {
                Write();
            }
            
        }
        
        public void Write()
        {
            lock (locker)
            {
                while (buff.TryDequeue(out string log))
                {
                    fs.WriteLine( DateTime.Now.ToString() + "|" + log);
                }

                fs.Flush();
            }
        }
    }
}
