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
    STALE
}