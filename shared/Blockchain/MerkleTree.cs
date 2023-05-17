using Kryolite.Shared.Blockchain;
using System.Security.Cryptography;

namespace Kryolite.Shared
{
    public class MerkleTree
    {
        HashTree root;

        public SHA256Hash RootHash { get => root.hash; }

        public MerkleTree(List<Transaction> transactions)
        {
            if (transactions.Count == 0) {
                root = new HashTree();
                return;
            }

            root = SetItems(transactions);
        }

        HashTree SetItems(List<Transaction> transactions)
        {
            transactions.OrderBy(x => x.TransactionId);

            var queue = new Queue<HashTree>();

            foreach(var transaction in transactions)
            {
                var hash = transaction.CalculateHash();
                var hashTree = new HashTree(hash);

                queue.Enqueue(hashTree);
            }

            if (queue.Count() % 2 == 1)
            {
                var repeat = new HashTree(queue.Last().hash);
                queue.Enqueue(repeat);
            }

            while (queue.Count() > 1)
            {
                var left = queue.Dequeue();
                var right = queue.Dequeue();
                
                var root = new HashTree();
                root.left = left;
                root.right = right;
                left.parent = root;
                right.parent = root;
                
                using var sha256 = SHA256.Create();

                root.hash = sha256.ComputeHash(left.hash.Concat(right.hash).ToArray());

                queue.Enqueue(root);
            }

            return queue.Dequeue();
        }
    }
}
