namespace Nofarma.Application.Idempotency;

public sealed class IdempotencyConflictException()
    : InvalidOperationException(
        "A chave idempotente já foi usada por uma operação diferente.");
