namespace Kryolite.Module.Validator;

internal interface ISynchronizer
{
    Task WaitForNextWindow(CancellationToken  cancellationToken);
    Task<bool> WaitForView(long height, CancellationToken cancellationToken);
}
