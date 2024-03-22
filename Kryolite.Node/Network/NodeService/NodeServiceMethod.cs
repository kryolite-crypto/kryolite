namespace Kryolite.Node.Network;

public enum NodeServiceMethod : byte
{
    GET_PEERS,
    GET_PUBLIC_KEY,
    GET_VIEW_FOR_ID,
    GET_VIEW_FOR_HASH,
    GET_BLOCK,
    GET_VOTE,
    SUGGEST_VIEW,
    FIND_COMMON_HEIGHT,
    GET_VIEWS_FOR_RANGE,
    SHOULD_SYNC,
    GENERATE_CHALLENGE
}
