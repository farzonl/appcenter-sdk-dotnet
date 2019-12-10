// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using SQLitePCL;

namespace Microsoft.AppCenter.Storage
{
    internal class StorageAdapter : IStorageAdapter
    {
        private sqlite3 _db;

        public void Initialize(string databasePath)
        {
            try
            {
                raw.SetProvider(new SQLite3Provider_e_sqlite3());
            }
            catch (Exception e)
            {
                throw new StorageException("Failed to initialize sqlite3 provider.", e);
            }
            var result = raw.sqlite3_open(databasePath, out _db);
            if (result != raw.SQLITE_OK)
            {
                throw ToStorageException(result, "Failed to open database connection");
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_db == null)
            {
                return;
            }
            _db.Dispose();
            _db = null;
        }

        private void BindParameter(sqlite3_stmt stmt, int index, object value)
        {
            int result;
            if (value is string)
            {
                result = raw.sqlite3_bind_text(stmt, index, (string)value);
            }
            else if (value is int)
            {
                result = raw.sqlite3_bind_int(stmt, index, (int)value);
            }
            else if (value is long)
            {
                result = raw.sqlite3_bind_int64(stmt, index, (long)value);
            }
            else
            {
                raw.sqlite3_finalize(stmt);
                throw new NotSupportedException($"Type {value.GetType().FullName} not supported.");
            }
            if (result != raw.SQLITE_OK)
            {
                raw.sqlite3_finalize(stmt);
                throw ToStorageException(result, $"Failed to bind {index} parameter");
            }
        }

        private void BindParameters(sqlite3_stmt stmt, IList<object> values)
        {
            for (var i = 0; i < values?.Count; i++)
            {
                // Parameters in statement are 1-based. See https://www.sqlite.org/c3ref/bind_blob.html
                BindParameter(stmt, i + 1, values[i]);
            }
        }

        private object GetColumnValue(sqlite3_stmt stmt, int index)
        {
            var columnType = raw.sqlite3_column_type(stmt, index);
            switch (columnType)
            {
                case raw.SQLITE_INTEGER:
                    return raw.sqlite3_column_int64(stmt, index);
                case raw.SQLITE_TEXT:
                    return raw.sqlite3_column_text(stmt, index);
            }
            AppCenterLog.Error(AppCenterLog.LogTag, $"Attempt to get unsupported column value {columnType}.");
            return null;
        }

        private int ExecuteNonSelectionSqlQuery(string query, IList<object> args = null)
        {
            var db = _db ?? throw new StorageException("The database wasn't initialized.");
            var result = raw.sqlite3_prepare_v2(db, query, out var stmt);
            if (result != raw.SQLITE_OK)
            {
                throw ToStorageException(result, "Failed to prepare SQL query");
            }
            BindParameters(stmt, args);
            result = raw.sqlite3_step(stmt);
            if (result != raw.SQLITE_DONE)
            {
                throw ToStorageException(result, "Failed to run query");
            }
            return raw.sqlite3_finalize(stmt);
        }

        private List<object[]> ExecuteSelectionSqlQuery(string query, IList<object> args = null)
        {
            var db = _db ?? throw new StorageException("The database wasn't initialized.");
            var entries = new List<object[]>();
            var result = raw.sqlite3_prepare_v2(db, query, out var stmt);
            if (result != raw.SQLITE_OK)
            {
                throw ToStorageException(result, "Failed to prepare SQL query");
            }
            BindParameters(stmt, args);
            while (raw.sqlite3_step(stmt) == raw.SQLITE_ROW)
            {
                var count = raw.sqlite3_column_count(stmt);
                entries.Add(Enumerable.Range(0, count).Select(i => GetColumnValue(stmt, i)).ToArray());
            }
            result = raw.sqlite3_finalize(stmt);
            if (result != raw.SQLITE_OK)
            {
                throw ToStorageException(result, "Failed to finalize SQL query");
            }
            return entries;
        }

        public void CreateTable(string tableName, string[] columnNames, string[] columnTypes)
        {
            var tableClause = string.Join(",", Enumerable.Range(0, columnNames.Length).Select(i => $"{columnNames[i]} {columnTypes[i]}"));
            var result = ExecuteNonSelectionSqlQuery($"CREATE TABLE IF NOT EXISTS {tableName} ({tableClause});");
            if (result != raw.SQLITE_OK)
            {
                throw ToStorageException(result, "Failed to create table");
            }
        }

        public int Count(string tableName, string columnName, object value)
        {
            var result = ExecuteSelectionSqlQuery($"SELECT COUNT(*) FROM {tableName} WHERE {columnName} = ?;", new[] { value });
            var count = (long)(result.FirstOrDefault()?.FirstOrDefault() ?? 0L);
            return (int)count;
        }

        public IList<object[]> Select(string tableName, string columnName, object value, string excludeColumnName, object[] excludeValues, int? limit = null)
        {
            var whereClause = $"{columnName} = ?";
            var args = new List<object> { value };
            if (excludeValues?.Length > 0)
            {
                whereClause += $" AND {excludeColumnName} NOT IN ({BuildBindingMask(excludeValues.Length)})";
                args.AddRange(excludeValues);
            }
            var limitClause = limit != null ? $" LIMIT {limit}" : string.Empty;
            var query = $"SELECT * FROM {tableName} WHERE {whereClause}{limitClause};";
            return ExecuteSelectionSqlQuery(query, args);
        }

        public void Insert(string tableName, string[] columnNames, ICollection<object[]> values)
        {
            var columnsClause = string.Join(",", columnNames);
            var valueClause = $"({BuildBindingMask(values.First().Length)})";
            var valuesClause = string.Join(",", Enumerable.Repeat(valueClause, values.Count));
            var valuesArray = values.SelectMany(i => i).ToArray();
            var result = ExecuteNonSelectionSqlQuery($"INSERT INTO {tableName}({columnsClause}) VALUES {valuesClause};", valuesArray);
            if (result != raw.SQLITE_OK)
            {
                throw ToStorageException(result, "Failed to prepare insert SQL query");
            }
        }

        public void Delete(string tableName, string columnName, params object[] values)
        {
            var whereMask = $"{columnName} IN ({BuildBindingMask(values.Length)})";
            var result = ExecuteNonSelectionSqlQuery($"DELETE FROM {tableName} WHERE {whereMask};", values);
            if (result != raw.SQLITE_OK)
            {
                throw ToStorageException(result, "Failed to prepare delete SQL query");
            }
        }

        private StorageException ToStorageException(int result, string message)
        {
            var errorMessage = raw.sqlite3_errmsg(_db);
            var exceptionMessage = $"{message}, result={result}\n\t{errorMessage}";
            if (result == raw.SQLITE_CORRUPT || result == raw.SQLITE_NOTADB)
            {
                return new StorageCorruptedException(exceptionMessage);
            }
            return new StorageException(exceptionMessage);
        }

        private static string BuildBindingMask(int amount)
        {
            return string.Join(",", Enumerable.Repeat("?", amount));
        }
    }
}
