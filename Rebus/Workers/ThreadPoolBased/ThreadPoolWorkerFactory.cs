﻿using System;
using System.Diagnostics;
using System.Threading;
using Rebus.Bus;
using Rebus.Config;
using Rebus.Logging;
using Rebus.Pipeline;
using Rebus.Threading;
using Rebus.Transport;

namespace Rebus.Workers.ThreadPoolBased
{
    public class ThreadPoolWorkerFactory : IWorkerFactory, IDisposable
    {
        readonly ITransport _transport;
        readonly IRebusLoggerFactory _rebusLoggerFactory;
        readonly IPipeline _pipeline;
        readonly IPipelineInvoker _pipelineInvoker;
        readonly Options _options;
        readonly Func<RebusBus> _busGetter;
        readonly ParallelOperationsManager _parallelOperationsManager;
        readonly ILog _log;

        public ThreadPoolWorkerFactory(ITransport transport, IRebusLoggerFactory rebusLoggerFactory, IPipeline pipeline, IPipelineInvoker pipelineInvoker, Options options, Func<RebusBus> busGetter)
        {
            if (transport == null) throw new ArgumentNullException(nameof(transport));
            if (rebusLoggerFactory == null) throw new ArgumentNullException(nameof(rebusLoggerFactory));
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));
            if (pipelineInvoker == null) throw new ArgumentNullException(nameof(pipelineInvoker));
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (busGetter == null) throw new ArgumentNullException(nameof(busGetter));
            _transport = transport;
            _rebusLoggerFactory = rebusLoggerFactory;
            _pipeline = pipeline;
            _pipelineInvoker = pipelineInvoker;
            _options = options;
            _busGetter = busGetter;
            _parallelOperationsManager = new ParallelOperationsManager(options.MaxParallelism);
            _log = _rebusLoggerFactory.GetCurrentClassLogger();

            if (_options.MaxParallelism < 1)
            {
                throw new ArgumentException($"Max parallelism is {_options.MaxParallelism} which is an invalid value");
            }

            if (options.WorkerShutdownTimeout < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException($"Cannot use '{options.WorkerShutdownTimeout}' as worker shutdown timeout as it");
            }
        }

        public IWorker CreateWorker(string workerName)
        {
            if (workerName == null) throw new ArgumentNullException(nameof(workerName));

            var owningBus = _busGetter();

            var worker = new ThreadPoolWorker(workerName, _transport, _rebusLoggerFactory, _pipeline, _pipelineInvoker, _parallelOperationsManager, owningBus);

            return worker;
        }

        public void Dispose()
        {
            if (!_parallelOperationsManager.HasPendingTasks) return;

            // give quick chance to finish working without logging anything
            Thread.Sleep(100);

            if (!_parallelOperationsManager.HasPendingTasks) return;

            // let the world know that we are waiting for something to finish
            _log.Info("Waiting for continuations to finish...");

            var stopwatch = Stopwatch.StartNew();
            var workerShutdownTimeout = _options.WorkerShutdownTimeout;

            while (true)
            {
                Thread.Sleep(100);

                if (!_parallelOperationsManager.HasPendingTasks)
                {
                    _log.Info("Done :)");
                    break;
                }

                if (stopwatch.Elapsed > workerShutdownTimeout)
                {
                    _log.Warn("Not all async tasks were able to finish within given timeout of {0} seconds!", workerShutdownTimeout.TotalSeconds);
                    break;
                }
            }
        }
    }
}