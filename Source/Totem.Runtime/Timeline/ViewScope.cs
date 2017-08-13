﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using Totem.Runtime.Map.Timeline;

namespace Totem.Runtime.Timeline
{
  /// <summary>
  /// The scope of a view's activity on the timeline
  /// </summary>
  internal sealed class ViewScope : FlowScope
  {
    readonly IViewExchange _exchange;
    readonly int _batchSize;
    int _batchCount;

    internal ViewScope(ILifetimeScope lifetime, TimelineScope timeline, IViewExchange exchange, FlowRoute initialRoute)
      : base(lifetime, timeline, initialRoute)
    {
      _exchange = exchange;

      _batchSize = ((ViewType) Key.Type).BatchSize;
    }

    protected override void OnWaitingForPoints() =>
      CompleteBatch();

    protected override async Task PushPoint()
    {
      AdvanceBatch();

      if(NotCompleted)
      {
        await PushBatchPoint();
      }
    }

    void AdvanceBatch()
    {
      if(_batchCount < _batchSize)
      {
        _batchCount++;
      }
      else
      {
        PushBatch();
      }
    }

    async Task PushBatchPoint()
    {
      try
      {
        await CallWhen();
      }
      catch(Exception error)
      {
        if(_batchCount > 0)
        {
          PushBatch();
        }

        await PushStopped(error);
      }
    }

    void PushBatch()
    {
      try
      {
        Timeline.PushView((View) Flow);

        if(_batchCount > 1)
        {
          Log.Verbose("[timeline] [{Key:l}] Pushed to timeline after batch of {BatchCount}", Key, _batchCount);
        }

        _batchCount = 0;
      }
      catch(Exception error)
      {
        Log.Error(error, "[timeline] [{Key:l}] Failed to push to timeline after batch of {BatchCount}", Key, _batchCount);

        CompleteTask(error);
      }
    }

    async Task PushStopped(Exception error)
    {
      Log.Error(error, "[timeline] [{Key:l}] Flow stopped", Key);

      try
      {
        Flow.Context.SetError(Point.Position);

        await Timeline.PushStopped(Point, error);

        CompleteTask(error);
      }
      catch(Exception pushError)
      {
        Log.Error(pushError, "[timeline] [{Key:l}] Failed to push {Stopped:l} to timeline", Key, Runtime.GetEvent(typeof(FlowStopped)));

        CompleteTask(new AggregateException(error, pushError));
      }
    }

    void CompleteBatch()
    {
      if(NotCompleted && _batchCount > 0)
      {
        PushBatch();
      }

      if(NotCompleted && !Resuming)
      {
        PushUpdate();
      }

      if(NotCompleted && Flow != null && Flow.Context.Done)
      {
        CompleteTask();
      }
    }

    void PushUpdate()
    {
      var view = (View) Flow;

      if(view != null)
      {
        try
        {
          _exchange.PushUpdate(view);
        }
        catch(Exception error)
        {
          Log.Error(error, "[timeline] [{Key:l}] Failed to push update", Key);
        }
      }
    }
  }
}