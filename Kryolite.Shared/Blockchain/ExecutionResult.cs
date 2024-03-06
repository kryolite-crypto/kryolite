using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Kryolite.Shared.Blockchain;

public enum ExecutionResult
{
    WAITING,
    VERIFYING,
    PENDING,
    SUCCESS,
    TOO_LOW_BALANCE,
    INVALID_CONTRACT,
    CONTRACT_EXECUTION_FAILED,
    CONTRACT_ENTRYPOINT_MISSING,
    CONTRACT_SNAPSHOT_MISSING,
    INVALID_PAYLOAD,
    INVALID_METHOD,
    VERIFY_FAILED,
    UNKNOWN,
    DUPLICATE_CONTRACT,
    STALE,
    ORPHAN,
    SCHEDULED
}

public static class ExecutionResultSerializer
{
    public static void Write(this Serializer serializer, ExecutionResult value)
    {
        serializer.Write((byte)value);
    }

    public static void Read(this Serializer serializer, ref ExecutionResult value)
    {
        byte b = 0;
        serializer.Read(ref b);
        value = (ExecutionResult)b;
    }
}