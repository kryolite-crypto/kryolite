namespace Kryolite.ByteSerializer;

public enum SerializerEnum : byte
{
    TRANSACTION,
    TRANSACTION_DTO,
    PUBLIC_KEY,
    PRIVATE_KEY,
    ADDRESS,
    SHA256,
    SIGNATURE,
    EFFECT,
    BLOCK,
    CHAINSTATE,
    LEDGER,
    BLOCKTEMPLATE,
    TOKEN,
    NODE_DTO,
    VOTE,
    VALIDATOR,
    VIEW,
    VOTE_BROADCAST,
    BLOCK_BROADCAST,
    TRANSACTION_BROADCAST,
    VIEW_BROADCAST,
    VIEW_RANGE_RESPONSE,
    VIEW_RESPONSE,
    AUTH_RESPONSE,
    AUTH_REQUEST,
    NODE_BROADCAST,
    PENDING_RESPONSE,
    TRANSACTION_PAYLOAD,
    CALL_METHOD,
    NEW_CONTRACT,
    CONTRACT,
    CONTRACT_MANIFEST,
    CONTRACT_METHOD,
    CONTRACT_PARAM,
    WALLET,
    ACCOUNT,
    MESSAGE,
    SYNC_REQUEST,
    SYNC_RESPONSE,
    BATCH_BROADCAST,
    MESSAGE_1,
    NODELIST_RESPONSE,
    VIEWLIST_RESPONSE,
    VIEWLIST_REQUEST,
    HASH_LIST
}