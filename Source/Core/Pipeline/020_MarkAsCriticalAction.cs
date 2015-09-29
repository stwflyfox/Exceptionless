﻿using System;
using System.Threading.Tasks;
using Exceptionless.Core.Component;
using Exceptionless.Core.Plugins.EventProcessor;
using NLog.Fluent;

namespace Exceptionless.Core.Pipeline {
    [Priority(20)]
    public class MarkAsCriticalAction : EventPipelineActionBase {
        protected override bool ContinueOnError => true;

        public override Task ProcessAsync(EventContext ctx) {
            if (ctx.Stack == null || !ctx.Stack.OccurrencesAreCritical)
                return TaskHelper.Completed();

            Log.Trace().Message("Marking error as critical.").Write();
            ctx.Event.MarkAsCritical();

            return TaskHelper.Completed();
        }
    }
}