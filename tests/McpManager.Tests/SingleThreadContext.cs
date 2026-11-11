using System.Collections.Concurrent;

namespace McpManager.Tests;

public sealed class SingleThreadContext : SynchronizationContext
{
  private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = [];

  public int ThreadId { get; private set; }

  public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

  public static T Run<T>(Func<Task<T>> action)
  {
    SingleThreadContext context = new();
    T result = default!;
    Exception? error = null;
    Thread thread = new(() =>
    {
      context.ThreadId = Environment.CurrentManagedThreadId;
      SetSynchronizationContext(context);
      Task<T> task = action();
      task.ContinueWith(_ => context._queue.CompleteAdding(), TaskScheduler.Default);
      foreach ((SendOrPostCallback callback, object? state) in context._queue.GetConsumingEnumerable())
      {
        callback(state);
      }

      try
      {
        result = task.GetAwaiter().GetResult();
      }
      catch (Exception ex)
      {
        error = ex;
      }
    });
    thread.Start();
    thread.Join();
    if (error != null)
    {
      throw error;
    }

    return result;
  }
}
