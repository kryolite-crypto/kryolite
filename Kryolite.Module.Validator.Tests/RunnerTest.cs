using Kryolite.Interface;
using Kryolite.Module.Validator;
using Kryolite.Type;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace Kryolite.Validator.Tests;

public class RunnerTests
{
    private readonly Mock<IKeyRepository> _keyRepositoryMock;
    private readonly Mock<ILogger<Runner>> _loggerMock;
    private readonly Mock<ISynchronizer> _synchronizerMock;
    private readonly Mock<IStoreManager> _storeManagerMock;
    private readonly Mock<ISlots> _slotsMock;
    private readonly Mock<IGenerator> _generatorMock;
    private readonly PublicKey _nodeKey;
    private readonly IServiceProvider _serviceProvider;

    public RunnerTests()
    {
        _keyRepositoryMock = new Mock<IKeyRepository>();
        _loggerMock = new Mock<ILogger<Runner>>();
        _synchronizerMock = new Mock<ISynchronizer>();
        _storeManagerMock = new Mock<IStoreManager>();
        _slotsMock = new Mock<ISlots>();
        _generatorMock = new Mock<IGenerator>();
        _nodeKey = PublicKey.Random;

        var sc = new ServiceCollection();

        sc.AddSingleton(_synchronizerMock.Object);
        sc.AddSingleton(_storeManagerMock.Object);
        sc.AddSingleton(_slotsMock.Object);
        sc.AddSingleton(_generatorMock.Object);

        _keyRepositoryMock.Setup(kr => kr.GetPublicKey()).Returns(_nodeKey);
        _serviceProvider = sc.BuildServiceProvider();
    }

    [Fact]
    public async Task Execute_ShouldWaitForEnabledTask_Enabled()
    {
        var cts = new CancellationTokenSource();
        var token = cts.Token;
        var runner = new Runner(_serviceProvider, _keyRepositoryMock.Object, _loggerMock.Object);

        var delay = 0;
        _synchronizerMock.Setup(s => s.WaitForNextWindow(token))
            .Returns(() => Task.Delay(Interlocked.Exchange(ref delay, 30_000), token));

        _ = runner.Execute(token);
        runner.Enable();

        await Task.Delay(100);

        _synchronizerMock.Verify(s => s.WaitForNextWindow(token), Times.Once);

        runner.Disable();
        cts.Cancel();
    }

    [Fact]
    public async Task Execute_ShouldWaitForEnabledTask_NotEnabled()
    {
        var cts = new CancellationTokenSource();
        var token = cts.Token;
        var runner = new Runner(_serviceProvider, _keyRepositoryMock.Object, _loggerMock.Object);

        var delay = 0;
        _synchronizerMock.Setup(s => s.WaitForNextWindow(token))
            .Returns(() => Task.Delay(Interlocked.Exchange(ref delay, 30_000), token));

        _ = runner.Execute(token);

        await Task.Delay(100);

        _synchronizerMock.Verify(s => s.WaitForNextWindow(token), Times.Never);

        runner.Disable();
        cts.Cancel();
    }

    [Fact]
    public async Task Execute_ShouldAssignSelfAsLeader_WhenNextLeaderCannotBeDetermined()
    {
        var cts = new CancellationTokenSource();
        var token = cts.Token;
        var runner = new Runner(_serviceProvider, _keyRepositoryMock.Object, _loggerMock.Object);

        var nextLeader = new Leader
        {
            PublicKey = PublicKey.NULL_PUBLIC_KEY,
            Height = 6,
            SlotNumber = 0,
            VoteCount = 1,
            VoteHeight = 5
        };

        var delay = 0;
        _synchronizerMock.Setup(s => s.WaitForNextWindow(token))
            .Returns(() => Task.Delay(Interlocked.Exchange(ref delay, 30_000), token));

        _slotsMock.Setup(s => s.TryGetNextLeader(out nextLeader))
            .Returns(false);

        _synchronizerMock.Setup(s => s.WaitForView(6, token))
            .ReturnsAsync(true);

        _ = runner.Execute(token);
        runner.Enable();

        await Task.Delay(100); // Simulate some delay

        Assert.Equal(_nodeKey, nextLeader.PublicKey);
        _generatorMock.Verify(g => g.GenerateView(), Times.Once);

        runner.Disable();
        cts.Cancel();
    }

    [Fact]
    public async Task Execute_ShouldGenerateView_WhenSelfIsLeader()
    {
        var cts = new CancellationTokenSource();
        var token = cts.Token;
        var runner = new Runner(_serviceProvider, _keyRepositoryMock.Object, _loggerMock.Object);

        var nextLeader = new Leader
        {
            PublicKey = _nodeKey,
            Height = 6,
            SlotNumber = 0,
            VoteCount = 1,
            VoteHeight = 5
        };

        var delay = 0;
        _synchronizerMock.Setup(s => s.WaitForNextWindow(token))
            .Returns(() => Task.Delay(Interlocked.Exchange(ref delay, 30_000), token));

        _slotsMock.Setup(s => s.TryGetNextLeader(out nextLeader))
            .Returns(true);

        _synchronizerMock.Setup(s => s.WaitForView(6, token))
            .ReturnsAsync(true);

        _ = runner.Execute(token);
        runner.Enable();

        await Task.Delay(100); // Simulate some delay

        _generatorMock.Verify(g => g.GenerateView(), Times.Once);

        runner.Disable();
        cts.Cancel();
    }

    [Fact]
    public async Task Execute_ShouldNotGenerateView_WhenSelfIsNotLeader()
    {
        var cts = new CancellationTokenSource();
        var token = cts.Token;
        var runner = new Runner(_serviceProvider, _keyRepositoryMock.Object, _loggerMock.Object);

        var nextLeader = new Leader
        {
            PublicKey = PublicKey.Random,
            Height = 6,
            SlotNumber = 0,
            VoteCount = 1,
            VoteHeight = 5
        };

        var delay = 0;
        _synchronizerMock.Setup(s => s.WaitForNextWindow(token))
            .Returns(() => Task.Delay(Interlocked.Exchange(ref delay, 30_000)));

        _slotsMock.Setup(s => s.TryGetNextLeader(out nextLeader))
            .Returns(true);

        _synchronizerMock.Setup(s => s.WaitForView(6, token))
            .ReturnsAsync(true);

        _ = runner.Execute(token);
        runner.Enable();

        await Task.Delay(100); // Simulate some delay

        _generatorMock.Verify(g => g.GenerateView(), Times.Never);

        runner.Disable();
        cts.Cancel();
    }

    [Fact]
    public async Task Execute_ShouldBanLeader_WhenViewCreationFails()
    {
        var cts = new CancellationTokenSource();
        var token = cts.Token;
        var runner = new Runner(_serviceProvider, _keyRepositoryMock.Object, _loggerMock.Object);

        var otherValidator = PublicKey.Random;

        var nextLeader = new Leader
        {
            PublicKey = otherValidator,
            Height = 6,
            SlotNumber = 0,
            VoteCount = 1,
            VoteHeight = 5
        };

        var delay = 0;
        _synchronizerMock.Setup(s => s.WaitForNextWindow(token))
            .Returns(() => Task.Delay(Interlocked.Exchange(ref delay, 30_000), token));

        _slotsMock.Setup(s => s.TryGetNextLeader(out nextLeader))
            .Returns(true);

        _synchronizerMock.Setup(s => s.WaitForView(6, token))
            .ReturnsAsync(false);

        _ = runner.Execute(token);
        runner.Enable();

        await Task.Delay(100); // Simulate some delay

        _slotsMock.Verify(s => s.Ban(otherValidator), Times.AtLeastOnce);

        runner.Disable();
        cts.Cancel();
    }
}