using System;
using System.Threading;
using Avalonia.Win32.DComposition;
using Xunit;

namespace Avalonia.IntegrationTests.Win32;

public class CompositionTransactionTests
{
    [Fact]
    public void Failed_Commit_Releases_Compositor_Lock()
    {
        var gate = new object();
        var transaction = DirectCompositedWindow.BeginTransaction(gate,
            () => throw new InvalidOperationException("Commit failed"));
        Assert.True(Monitor.IsEntered(gate));

        Assert.Throws<InvalidOperationException>(() => transaction.Dispose());

        Assert.False(Monitor.IsEntered(gate));
    }
}
