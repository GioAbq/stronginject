using System;
using System.Collections.Generic;
using System.Text;

namespace StrongInject
{
    public sealed class StrongInjectException : Exception
    {
        public StrongInjectException()
        {
        }

        public StrongInjectException(string message) : base(message)
        {
        }

        public StrongInjectException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
