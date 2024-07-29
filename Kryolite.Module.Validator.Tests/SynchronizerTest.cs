using Kryolite.EventBus;
using Kryolite.Interface;
using Kryolite.Module.Validator;
using Kryolite.Shared.Blockchain;
using Kryolite.Type;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;

namespace Kryolite.Validator.Tests;

public class ValidatorServiceTests
{
    private readonly IServiceProvider _serviceProvider;
    private readonly Mock<IRunner> _runnerMock;
    private readonly Mock<IKeyRepository> _keyRepositoryMock;
    private readonly Mock<IEventBus> _eventBusMock;
    private readonly Mock<IHostApplicationLifetime> _lifetimeMock;
    private readonly Mock<ILogger<ValidatorService>> _loggerMock;
    private readonly Mock<IStoreManager> _storeManagerMock;
    private readonly ValidatorService _validator;
    private readonly PublicKey _nodeKey = PublicKey.Random;

    public ValidatorServiceTests()
    {
        _runnerMock = new Mock<IRunner>();
        _keyRepositoryMock = new Mock<IKeyRepository>();
        _eventBusMock = new Mock<IEventBus>();
        _lifetimeMock = new Mock<IHostApplicationLifetime>();
        _loggerMock = new Mock<ILogger<ValidatorService>>();
        _storeManagerMock = new Mock<IStoreManager>();

        _keyRepositoryMock.Setup(kr => kr.GetPublicKey())
            .Returns(_nodeKey);

        using var cts = new CancellationTokenSource();

        var applicationStarted = new Mock<IHostApplicationLifetime>();
        _lifetimeMock.Setup(l => l.ApplicationStarted)
            .Returns(cts.Token);

        var sc = new ServiceCollection();
        sc.AddSingleton(_storeManagerMock.Object);

        _serviceProvider = sc.BuildServiceProvider();

        _validator = new ValidatorService(
            _serviceProvider, 
            _runnerMock.Object, 
            _keyRepositoryMock.Object, 
            _eventBusMock.Object, 
            _lifetimeMock.Object, 
            _loggerMock.Object
        );

        // Simulate application start
        cts.Cancel();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldSubscribeToEvents_AndStartRunner()
    {
        var cts = new CancellationTokenSource();
        var token = cts.Token;

        _storeManagerMock.Setup(sm => sm.IsValidator(_nodeKey.ToAddress()))
            .Returns(true);

        _ = _validator.StartAsync(token);

        await Task.Delay(100);

        _eventBusMock.Verify(eb => eb.Subscribe(It.IsAny<Action<ValidatorEnable>>()), Times.Once);
        _eventBusMock.Verify(eb => eb.Subscribe(It.IsAny<Action<ValidatorDisable>>()), Times.Once);
        _runnerMock.Verify(r => r.Execute(It.IsAny<CancellationToken>()), Times.Once);

        cts.Cancel();
    }

    [Fact]
    public void IsNodeValidator_ShouldReturnTrue()
    {
        _storeManagerMock.Setup(sm => sm.IsValidator(_nodeKey.ToAddress()))
            .Returns(true);

        var result = _validator.IsNodeValidator();

        Assert.True(result);
    }

    [Fact]
    public void IsNodeValidator_ShouldReturnFalse()
    {
        _storeManagerMock.Setup(sm => sm.IsValidator(_nodeKey.ToAddress()))
            .Returns(false);

        var result = _validator.IsNodeValidator();

        Assert.False(result);
    }

    [Fact]
    public void Enable_ShouldCallRunnerEnable()
    {
        _validator.Enable();
        _runnerMock.Verify(r => r.Enable(), Times.Once);
    }

    [Fact]
    public void Disable_ShouldCallRunnerDisable()
    {
        _validator.Disable();
        _runnerMock.Verify(r => r.Disable(), Times.Once);
    }
}
