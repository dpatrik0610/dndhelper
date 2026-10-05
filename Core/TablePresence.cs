using dndhelper.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace dndhelper.Core
{
    /// <summary>
    /// Who is connected to which table, by connection. A connection counts as seated only while it is listed here,
    /// so removing it (leave, disconnect, kick) also takes away its access to the table.
    /// In memory: like the hub groups, it needs a backplane-backed store to scale past one server.
    /// </summary>
    public sealed class TablePresence
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, Dictionary<string, TableContext>> _tables = new();

        public void Add(string connectionId, TableContext ctx)
        {
            lock (_gate)
            {
                if (!_tables.TryGetValue(ctx.TableId, out var seats))
                    _tables[ctx.TableId] = seats = new Dictionary<string, TableContext>();
                seats[connectionId] = ctx;
            }
        }

        public void Remove(string connectionId, string tableId)
        {
            lock (_gate)
            {
                if (_tables.TryGetValue(tableId, out var seats) && seats.Remove(connectionId) && seats.Count == 0)
                    _tables.Remove(tableId);
            }
        }

        public bool Contains(string connectionId, string tableId)
        {
            lock (_gate)
                return _tables.TryGetValue(tableId, out var seats) && seats.ContainsKey(connectionId);
        }

        /// <summary>One entry per person, however many tabs they have open. DMs first, then by name.</summary>
        public List<TableParticipant> Participants(string tableId)
        {
            lock (_gate)
            {
                if (!_tables.TryGetValue(tableId, out var seats)) return new List<TableParticipant>();
                return seats.Values
                    .GroupBy(s => s.UserId)
                    .Select(g => new TableParticipant(g.Key, g.First().Name, g.Any(s => s.IsDm)))
                    .OrderByDescending(p => p.IsDm)
                    .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }

        public List<string> Connections(string tableId, Func<TableContext, bool> match)
        {
            lock (_gate)
                return _tables.TryGetValue(tableId, out var seats)
                    ? seats.Where(s => match(s.Value)).Select(s => s.Key).ToList()
                    : new List<string>();
        }

        /// <summary>Unseats every matching connection and returns them.</summary>
        public List<(string ConnectionId, TableContext Seat)> RemoveWhere(string tableId, Func<TableContext, bool> match)
        {
            lock (_gate)
            {
                if (!_tables.TryGetValue(tableId, out var seats)) return new();
                var removed = seats.Where(s => match(s.Value)).Select(s => (s.Key, s.Value)).ToList();
                foreach (var (connectionId, _) in removed) seats.Remove(connectionId);
                if (seats.Count == 0) _tables.Remove(tableId);
                return removed;
            }
        }
    }
}
