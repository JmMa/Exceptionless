﻿using System;
using System.Threading.Tasks;
using Exceptionless;
using Exceptionless.Insulation.Jobs;
using Foundatio.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace StackEventCountJob {
    public class Program {
        public static async Task<int> Main() {
            try {
                var serviceProvider = JobServiceProvider.GetServiceProvider();
                var job = serviceProvider.GetService<Exceptionless.Core.Jobs.StackEventCountJob>();
                return await new JobRunner(
                    serviceProvider.GetService<Exceptionless.Core.Jobs.StackEventCountJob>(), 
                    serviceProvider.GetRequiredService<ILoggerFactory>(), 
                    initialDelay: TimeSpan.FromSeconds(2), 
                    interval: TimeSpan.FromSeconds(5)
                ).RunInConsoleAsync();
            }
            catch (Exception ex) {
                Log.Fatal(ex, "Job terminated unexpectedly");
                return 1;
            }
            finally {
                Log.CloseAndFlush();
                await ExceptionlessClient.Default.ProcessQueueAsync();
            }
        }
    }
}
