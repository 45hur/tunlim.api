using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Text;

using tunlim.api.Models;

using CsvHelper;
using Newtonsoft.Json;

namespace tunlim.api
{
    internal class WebAPI
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private ConcurrentQueue<Job> queue = new ConcurrentQueue<Job>();
        private ConcurrentQueue<Job> priorityQueue = new ConcurrentQueue<Job>();
        private Thread tListener;

        protected readonly string dbpath;

        public WebAPI()
        {
            dbpath = (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                ? @"C:\lmdb\"
                : @"/var/whalebone/lmdb/";

            if (!Directory.Exists(dbpath))
            {
                Directory.CreateDirectory(dbpath);
            }
        }

        [Mapping("meminfo")]
        public object MemoryInfo(HttpListenerContext ctx, string postdata)
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    using (var filestream = File.OpenRead("/proc/meminfo"))
                    {
                        using (var sr = new StreamReader(filestream))
                        {
                            var text = sr.ReadToEnd();
                            var lines = text.Split("\n");
                            var mem = lines.Where(t => t.Contains("MemAvailable")).First();
                            return GenerateSuccess(mem);
                        }
                    }
                }

                return GenerateError("Platform is not yet supported.");
            }
            catch (Exception ex)
            {
                log.Error(ex);

                return GenerateError(ex.Message);
            }
        }

        [Mapping("select")]
        public object Select(HttpListenerContext ctx, string postdata, string db, string table, UInt64 key)
        {
            try
            {
                using (var lmdb = new Lightning(Path.Combine(dbpath, db), 1))
                {
                    var keybytes = BitConverter.GetBytes(key);
                    var value = lmdb.Get(table, keybytes);
                    if (value == null)
                    {
                        return GenerateSuccess($"Table contains no elements for key {key}");
                    }
                    var valuestr = Encoding.UTF8.GetString(value);

                    return GenerateSuccess(valuestr);
                }
            }
            catch (Exception ex)
            {
                log.Error(ex);

                return GenerateError(ex.Message);
            }
        }

        [Mapping("add")]
        public object Add(HttpListenerContext ctx, string postdata, string db, string table, UInt64 key, string value)
        {
            try
            {
                var keybytes = BitConverter.GetBytes(key);
                var valuebytes = Encoding.UTF8.GetBytes(value);
                using (var lmdb = new Lightning(Path.Combine(dbpath, db), 1))
                {
                    lmdb.Put(table, keybytes, valuebytes);
                }

                return GenerateSuccess($"Inserted into {table}, key={key}, value={value}.");
            }
            catch (Exception ex)
            {
                log.Error(ex);

                return GenerateError(ex.Message);
            }
        }

        [Mapping("delete")]
        public object Delete(HttpListenerContext ctx, string postdata, string db, string table, UInt64 key)
        {
            try
            {
                var keybytes = BitConverter.GetBytes(key);
                using (var lmdb = new Lightning(Path.Combine(dbpath, db), 1))
                {
                    lmdb.Delete(table, keybytes);

                    return GenerateSuccess($"Deleted {key} from {table}.");
                }
            }
            catch (Exception ex)
            {
                log.Error(ex);

                return GenerateError(ex.Message);
            }
        }

        [Mapping("getkeys")]
        public object GetKeys(HttpListenerContext ctx, string postdata, string db, string table)
        {
            try
            {
                using (var lmdb = new Lightning(Path.Combine(dbpath, db), 1))
                {
                    var values = lmdb.GetKeys(table);

                    return GenerateSuccess(string.Join(",", values));
                }
            }
            catch (Exception ex)
            {
                log.Error(ex);

                return GenerateError(ex.Message);
            }
        }

        private object GenerateSuccess(string message)
        {
            var result = new StringBuilder();
            result.AppendLine($"{{");
            result.AppendLine($"    \"result\": \"success\",");
            result.AppendLine($"    \"message\": \"{message}\"");
            result.AppendLine($"}}");
            return result.ToString();
        }


        private object GenerateError(string message)
        {
            var result = new StringBuilder();
            result.AppendLine($"{{");
            result.AppendLine($"    \"result\": \"error\",");
            result.AppendLine($"    \"message\": \"{message}\"");
            result.AppendLine($"}}");
            throw new HttpException(result.ToString(), HttpStatusCode.InternalServerError);
        }

        private void ThreadProcListener()
        {
            log.Info("Starting Listener thread.");

            if (string.IsNullOrEmpty(Configuration.GetApiServer()))
            {
                log.Info("listener (configuration) not set, Listener thread exiting.");
                return;
            }

            try
            {
                while (true)
                {
                    var prefix = Configuration.GetApiServer();
                    log.Info($"Starting WebAPI on {prefix}");
                    var listener = new HttpListener();

                    listener.Prefixes.Add(prefix);
                    listener.Start();
                    while (true)
                    {
                        var ctx = listener.GetContext();

                        ThreadPool.QueueUserWorkItem((_) =>
                        {
                            try
                            {
                                if (ctx.Request.RemoteEndPoint != null)
                                    log.Info($"RemoteEndPoint = {ctx.Request.RemoteEndPoint}");

                                var methodName = ctx.Request.Url.Segments[1].Replace("/", "");
                                var strParams = ctx.Request.Url
                                                        .Segments
                                                        .Skip(2)
                                                        .Select(s => s.Replace("/", ""))
                                                        .ToArray();

                                var method = this
                                                    .GetType()
                                                    .GetMethods()
                                                    .First(mi => mi.GetCustomAttributes(true).Any(attr => attr is Mapping mapping && mapping.Map == methodName));

                                var args = method.GetParameters().Skip(2).Select((p, i) => Convert.ChangeType(strParams[i], p.ParameterType));
                                var @params = new object[args.Count() + 2];

                                var inLength = ctx.Request.ContentLength64;

                                var inBuffer = new byte[4096];
                                var buffer = new byte[inLength];
                                var totalBytesRead = 0;
                                while (true)
                                {
                                    var bytesRead = ctx.Request.InputStream.Read(inBuffer, 0, inBuffer.Length);
                                    if (bytesRead == 0 || bytesRead == -1)
                                    {
                                        break;
                                    }

                                    Array.Copy(inBuffer, 0, buffer, totalBytesRead, bytesRead);
                                    totalBytesRead += bytesRead;

                                    if (totalBytesRead == inLength)
                                    {
                                        break;
                                    }
                                }

                                @params[0] = ctx;
                                @params[1] = Encoding.UTF8.GetString(buffer);
                                Array.Copy(args.ToArray(), 0, @params, 2, args.Count());

                                log.Info($"Invoking {method.Name}");
                                if (method.Invoke(this, @params) is string ret)
                                {
                                    var outBuffer = Encoding.UTF8.GetBytes(ret);
                                    ctx.Response.ContentType = "application/json";
                                    ctx.Response.AppendHeader("Access-Control-Allow-Origin", "*");
                                    ctx.Response.AppendHeader("Access-Control-Allow-Headers", "Content-Type");
                                    ctx.Response.ContentLength64 = outBuffer.LongLength;
                                    ctx.Response.OutputStream.Write(outBuffer, 0, outBuffer.Length);
                                }

                                ctx.Response.OutputStream.Close();

                            }
                            catch (Exception ex)
                            {
                                if (ex.InnerException != null && ex.InnerException is HttpException hex)
                                {
                                    log.Error($"Unable to process request {ctx.Request.Url}, ex: {ex.InnerException}");

                                    try
                                    {
                                        ctx.Response.StatusCode = (int)hex.Status;
                                        ctx.Response.ContentType = "application/json";

                                        var outBuffer = Encoding.UTF8.GetBytes(hex.Message);
                                        ctx.Response.ContentLength64 = outBuffer.LongLength;
                                        ctx.Response.OutputStream.Write(outBuffer, 0, outBuffer.Length);
                                    }
                                    catch
                                    {
                                        //ignored
                                    }
                                }
                                else
                                {
                                    log.Error($"Unable to process request {ctx.Request.Url}, ex: {ex}");

                                    try
                                    {
                                        ctx.Response.StatusCode = 500;
                                        ctx.Response.ContentType = "text/plain";

                                        var outBuffer = Encoding.UTF8.GetBytes(ex.Message);
                                        ctx.Response.ContentLength64 = outBuffer.LongLength;
                                        ctx.Response.OutputStream.Write(outBuffer, 0, outBuffer.Length);
                                    }
                                    catch
                                    {
                                        //ignored
                                    }
                                }

                                ctx.Response.OutputStream.Close();
                            }
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                tListener = null;

                log.Fatal($"{ex}");
            }
        }


        private void ThreadProcProcessor()
        {
            log.Info("Starting Processor thread.");

            while (true)
            {
                Thread.Sleep(500);

                try
                {
                    Job job = null;
                    if (queue.TryDequeue(out job))
                    {
                        if (job.ExecutionTime > DateTime.UtcNow)
                        {
                            queue.Enqueue(job);
                            continue;
                        }

                        log.Debug($"Running job {job.ID} {job.data}");
                        object arg = job;
                        var task = new TaskFactory().StartNew(new Action<object>((targ) =>
                        {
                            try
                            {

                            }
                            catch (Exception ex)
                            {
                                log.Error($"Task failed, ex: {ex}");
                            }
                        }), arg);
                    }
                }
                catch (Exception ex)
                {
                    log.Error($"Processor error: {ex}");
                }
            }
        }

        public void Listen()
        {
            tListener = new Thread(ThreadProcListener);
            tListener.Start();

            tListener = new Thread(ThreadProcProcessor);
            tListener.Start();
        }

    }
}
