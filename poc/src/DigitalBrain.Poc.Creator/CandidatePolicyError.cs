namespace DigitalBrain.Poc.Creator;

public enum CandidatePolicyError
{
    None,
    FixedHeaderMismatch,
    ForbiddenConstruct,
    ForbiddenSymbol,
    ForbiddenConstructor,
    UnauthorizedTrigger,
    UnauthorizedOutput,
    AliasCollision,
    UnauthorizedTarget,
    RecursiveCall,
    CompilationError,
    InvalidShape,
}
