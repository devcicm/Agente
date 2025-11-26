using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace IPC.DB.Services
{
    public sealed class SqlDatabaseExecutor : IDatabaseExecutor
    {
        private readonly string _connectionString;
        private readonly ILogger<SqlDatabaseExecutor> _logger;

        public SqlDatabaseExecutor(string connectionString, ILogger<SqlDatabaseExecutor> logger)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _logger = logger;
        }

        public async Task<int> ExecuteAsync(string sql, IDictionary<string, object?>? parameters = null, CancellationToken cancellationToken = default)
        {
            await using var connection = new SqlConnection(_connectionString);
            await using var command = connection.CreateCommand();
            PrepareCommand(command, sql, parameters);

            _logger.LogInformation("Ejecutando comando SQL: {Sql}", sql);
            await connection.OpenAsync(cancellationToken);
            var rows = await command.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Comando completado. Filas afectadas: {Rows}", rows);
            return rows;
        }

        public async Task<IReadOnlyList<IDictionary<string, object?>>> QueryAsync(string sql, IDictionary<string, object?>? parameters = null, CancellationToken cancellationToken = default)
        {
            await using var connection = new SqlConnection(_connectionString);
            await using var command = connection.CreateCommand();
            PrepareCommand(command, sql, parameters);

            _logger.LogInformation("Ejecutando consulta SQL: {Sql}", sql);
            await connection.OpenAsync(cancellationToken);
            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken);
            var result = new List<IDictionary<string, object?>>();

            while (await reader.ReadAsync(cancellationToken))
            {
                var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    var value = await reader.IsDBNullAsync(i, cancellationToken) ? null : reader.GetValue(i);
                    row[reader.GetName(i)] = value;
                }
                result.Add(row);
            }

            _logger.LogInformation("Consulta completada. Registros obtenidos: {Count}", result.Count);
            return result;
        }

        private static void PrepareCommand(SqlCommand command, string sql, IDictionary<string, object?>? parameters)
        {
            command.CommandText = sql ?? throw new ArgumentNullException(nameof(sql));
            command.CommandType = CommandType.Text;

            if (parameters is null)
            {
                return;
            }

            foreach (var kvp in parameters)
            {
                var parameter = command.Parameters.AddWithValue(kvp.Key, kvp.Value ?? DBNull.Value);
            }
        }
    }
}
