using System;
using System.Windows.Threading;
using Gander.Core.Controller;

namespace Gander.App.Hosting;

/// <summary>The WPF dispatcher as the controller's main thread. <see cref="Post"/> always queues.</summary>
public sealed class DispatcherMainThread(Dispatcher dispatcher) : IMainThread
{
    /// <inheritdoc />
    public void Post(Action action) => dispatcher.BeginInvoke(DispatcherPriority.Normal, action);
}
