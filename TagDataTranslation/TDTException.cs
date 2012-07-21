using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FOSSTRAK.TDT
{

    /// <summary>
    /// A TDTException is thrown at runtime when the user supplies invalid or incomplete input.
    /// </summary>
    /// <remarks>Author Mike Lohmeier myname@gmail.com</remarks>
    public class TDTException : Exception
    {
        public TDTException(String message)
            : base(message)
        {
        }

    }
}
