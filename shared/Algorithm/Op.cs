namespace Kryolite.Shared.Algorithm;

public enum Op
{
    /// <summary>
    /// Load local value from given location onto stack
    /// </summary>
    LdLoc,

    /// <summary>
    /// Pop value from stack and store it into given location
    /// </summary>
    StLoc,

    /// <summary>
    /// Load Int32 value onto stack
    /// </summary>
    LdInt32,

    /// <summary>
    /// Load Int64 value onto stack
    /// </summary>
    LdInt64,

    /// <summary>
    /// Duplica value on stack
    /// </summary>
    Dup,

    /// <summary>
    /// Pop 2 values from stack, perform addition and push result back to stack
    /// </summary>
    Add,

    /// <summary>
    /// Pop 2 values from stack, perform subtraction and push result back to stack
    /// </summary>
    Sub,

    /// <summary>
    /// Pop 2 values from stack, perform multiplication and push result back to stack
    /// </summary>
    Mul,

    /// <summary>
    /// Pop 2 values from stack, perform bitwise AND and push result back to stack
    /// </summary>
    And,

    /// <summary>
    /// Pop single value from stack, perform bitwise NOT and push result back to stack
    /// </summary>
    Not,

    /// <summary>
    /// Pop 2 values from stack, perform bitwise OR and push result back to stack
    /// </summary>
    Or,

    /// <summary>
    /// Pop 2 values from stack, perform bitwise XOR and push result back to stack
    /// </summary>
    Xor,

    /// <summary>
    /// Pop 2 values from stack, rotate left and push result back to stack
    /// </summary>
    Rotl,

    /// <summary>
    /// Pop 2 values from stack, rotate right and push result back to stack
    /// </summary>
    Rotr,

    /// <summary>
    /// Pop 2 values from stack, shift right and push result back to stack
    /// </summary>
    ShrUn,

    /// <summary>
    /// Pop 2 values from stack, shift left and push result back to stack
    /// </summary>
    Shl,

    /// <summary>
    /// Pop last item from stack and return it
    /// </summary>
    Ret
}
