using System;
using System.Net;

namespace tunlim.api
{
    public class HttpException : Exception
    {
        public HttpStatusCode Status { get; internal set; }
        public HttpException(string message, HttpStatusCode status) : base(message)
        {
            this.Status = status;
        }
    }
}
