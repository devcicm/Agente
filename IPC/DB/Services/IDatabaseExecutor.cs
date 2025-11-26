using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IPC.DB.Services
{
    public interface IDatabaseExecutor
    {
        Task<int> ExecuteAsync(string sql, IDictionary<string, object?>? parameters = null, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<IDictionary<string, object?>>> QueryAsync(string sql, IDictionary<string, object?>? parameters = null, CancellationToken cancellationToken = default);
    }
}
