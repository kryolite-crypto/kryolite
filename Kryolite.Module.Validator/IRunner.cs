namespace Kryolite.Module.Validator;

internal interface IRunner
{
    Task Execute(CancellationToken cancellationToken);
    void Enable();
    void Disable();
}
