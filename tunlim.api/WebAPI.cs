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
        private Thread tProcessor;

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

        [Mapping("key")]
        public object Key(HttpListenerContext ctx, string postdata)
        {
            try
            {
                var lmdb = new Lightning("/var/whalebone/tunlim", 1);
                var key = BitConverter.GetBytes(Convert.ToUInt64(postdata));
                var value = Encoding.UTF8.GetString(lmdb.Get("cache", key));

                return GenerateSuccess(value);
            }
            catch (Exception ex)
            {
                log.Error(ex);

                return GenerateError(ex.Message);
            }
        }

        [Mapping("add")]
        public object Add(HttpListenerContext ctx, string postdata)
        {
            try
            {
                var pz = postdata.Split(";");
                if (pz.Length != 2)
                {
                    return GenerateError("Could not parse postdata, expected value example: \"123;value\"");
                }

                var key = BitConverter.GetBytes(Convert.ToUInt64(pz[0]));
                var value = Encoding.UTF8.GetBytes(pz[1]);
                var lmdb = new Lightning("/var/whalebone/tunlim", 1);

                lmdb.Put("cache", key, value);

                return GenerateSuccess($"Inserted {key} : {value}.");
            }
            catch (Exception ex)
            {
                log.Error(ex);

                return GenerateError(ex.Message);
            }
        }

        [Mapping("allkeys")]
        public object AllKeys(HttpListenerContext ctx, string postdata)
        {
            try
            {
                var lmdb = new Lightning("/var/whalebone/tunlim", 1);
                var values = lmdb.GetKeys(postdata);

                return GenerateSuccess(string.Join(",", values));
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
                    HttpListener listener = new HttpListener();

                    listener.Prefixes.Add(prefix);
                    listener.Start();
                    while (true)
                    {
                        HttpListenerContext ctx = listener.GetContext();

                        ThreadPool.QueueUserWorkItem((_) =>
                        {
                            try
                            {
                                log.Info($"RemoteEndPoint = {ctx.Request.RemoteEndPoint.ToString()}");

                                string methodName = ctx.Request.Url.Segments[1].Replace("/", "");
                                string[] strParams = ctx.Request.Url
                                                        .Segments
                                                        .Skip(2)
                                                        .Select(s => s.Replace("/", ""))
                                                        .ToArray();

                                MethodInfo method = null;

                                try
                                {
                                    method = this.GetType()
                                                        .GetMethods()
                                                        .Where(mi => mi.GetCustomAttributes(true).Any(attr => attr is Mapping && ((Mapping)attr).Map == methodName))
                                                        .First();
                                }
                                catch (Exception ex)
                                {
                                    log.Debug(ex);

                                    ctx.Response.OutputStream.Close();

                                    return;
                                }

                                var args = method.GetParameters().Skip(2).Select((p, i) => Convert.ChangeType(strParams[i], p.ParameterType));
                                var @params = new object[args.Count() + 2];

                                var inLength = ctx.Request.ContentLength64;

                                var inBuffer = new byte[4096];
                                var buffer = new byte[inLength];
                                int totalBytesRead = 0;
                                int bytesRead = 0;
                                while (true)
                                {
                                    bytesRead = ctx.Request.InputStream.Read(inBuffer, 0, inBuffer.Length);
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
                                try
                                {
                                    var ret = method.Invoke(this, @params) as string;
                                    if (ret != null)
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
                                catch (HttpException ex)
                                {
                                    log.Error($"{ex}");

                                    ctx.Response.StatusCode = (int)ex.Status;
                                    ctx.Response.ContentType = "application/json";

                                    var outBuffer = Encoding.UTF8.GetBytes(ex.Message);
                                    ctx.Response.ContentLength64 = outBuffer.LongLength;
                                    ctx.Response.OutputStream.Write(outBuffer, 0, outBuffer.Length);
                                    ctx.Response.OutputStream.Close();
                                }
                            }
                            catch (Exception ex)
                            {
                                log.Error($"Unable to process request {ctx.Request.Url}, ex: {ex}");

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
