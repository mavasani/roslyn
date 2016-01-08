using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Host.Mef;

namespace Microsoft.VisualStudio.LanguageServices.Implementation.CobyIntegration
{
    internal class CobyWorkspace : Workspace
    {
        public static readonly CobyWorkspace Instance = new CobyWorkspace();

        public CobyWorkspace() : base(MefHostServices.DefaultHost, "CobyWorkspace")
        {
            // need to change default host to contain services I want
        }
    }
}
