// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Security;
using System.Net.Test.Common;
using System.Runtime.InteropServices;
using System.Security.Authentication.ExtendedProtection;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Xunit;

namespace System.Net.Http.Functional.Tests
{
    public abstract partial class HttpClientHandler_ServerCertificates_Test
    {
        private static bool ShouldSuppressRevocationException
        {
            get
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    return false;
                }

                // If a run on a clean macOS ever fails we need to consider that "false"
                // for CheckCertificateRevocationList is actually "use a system default" now,
                // and may require changing how this option is exposed. Considering the variety of
                // systems this should probably be complex like
                // enum RevocationCheckingOption {
                //     // Use it if able
                //     BestPlatformSecurity = 0,
                //     // Don't use it, if that's an option.
                //     BestPlatformPerformance,
                //     // Required
                //     MustCheck,
                //     // Prohibited
                //     MustNotCheck,
                // }

                if (Interop.Http.GetSslVersionDescription() == "SecureTransport")
                {
                    return true;
                }
                return false;
            }
        }

        internal bool BackendSupportsCustomCertificateHandling
        {
            get
            {
                if (UseSocketsHttpHandler)
                {
                    return true;
                }

                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    return false;
                }

                // For other Unix-based systems it's true if (and only if) the openssl backend
                // is used with libcurl.
                return (Interop.Http.GetSslVersionDescription()?.StartsWith(Interop.Http.OpenSsl10Description, StringComparison.OrdinalIgnoreCase) ?? false);
            }
        }

        [Theory]
        [PlatformSpecific(~TestPlatforms.OSX)] // Not implemented
        [InlineData(false, false, false, false, false)] // system -> ok
        [InlineData(true, true, true, true, true)]      // empty dir, empty bundle file -> fail
        // It is enough to override the bundle, since all tested platforms don't have a default dir:
        [InlineData(false, false, true, true, true)]    // empty bundle -> fail
        [InlineData(false, false, true, false, true)]   // non-existing bundle -> fail
        public void HttpClientUsesSslCertEnvironmentVariables(bool setSslCertDir, bool createSslCertDir,
            bool setSslCertFile, bool createSslCertFile, bool expectedFailure)
        {
            if (expectedFailure && RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return; // [ActiveIssue(28002)]
            }

            // This test sets SSL_CERT_DIR and SSL_CERT_FILE to empty/non-existing locations and then
            // checks the http request fails.
            // Some platforms will use the system default when not specifying a value, while others
            // will not use those certificates. Due to these platform differences, we only check specific
            // combinations that are expected to work the same cross-platform.
            var psi = new ProcessStartInfo();
            if (setSslCertDir)
            {
                string sslCertDir = GetTestFilePath();
                if (createSslCertDir)
                {
                    Directory.CreateDirectory(sslCertDir);
                }
                psi.Environment.Add("SSL_CERT_DIR", sslCertDir);
            }

            if (setSslCertFile)
            {
                string sslCertFile = GetTestFilePath();
                if (createSslCertFile)
                {
                    File.WriteAllText(sslCertFile, "");
                }
                psi.Environment.Add("SSL_CERT_FILE", sslCertFile);
            }

            RemoteInvoke(async arg =>
            {
                bool shouldFail = bool.Parse(arg);
                const string Url = "https://www.microsoft.com";

                using (HttpClient client = new HttpClient())
                {
                    if (shouldFail)
                    {
                        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(Url));
                    }
                    else
                    {
                        await client.GetAsync(Url);
                    }
                }
                return SuccessExitCode;
            }, expectedFailure.ToString(), new RemoteInvokeOptions { StartInfo = psi }).Dispose();
        }
    }
}
