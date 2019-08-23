using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;

using LightningDB;

namespace tunlim.api
{
    public class Lightning : IDisposable
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        protected LightningEnvironment env;

        public Lightning(string envPath, int numOfDb)
        {
            env = new LightningEnvironment(envPath)
            {
                MaxDatabases = numOfDb
            };
            env.Open();
        }

        public void Dispose()
        {
            env.Dispose();
        }

        private byte[] ObjectToByteArray(object obj)
        {
            if (obj == null)
                return null;

            var bf = new BinaryFormatter();
            using (var ms = new MemoryStream())
            {
                bf.Serialize(ms, obj);
                return ms.ToArray();
            }
        }

        private object ByteArrayToObject(byte[] bytes)
        {
            if (bytes == null)
                return null;

            var bf = new BinaryFormatter();
            using (var ms = new MemoryStream(bytes))
            {
                return bf.Deserialize(ms);
            }
        }

        public void Put(string dbname, string key, string value)
        {
            using (var tx = env.BeginTransaction())
            {
                try
                {
                    using (var db = tx.OpenDatabase(dbname, new DatabaseConfiguration { Flags = DatabaseOpenFlags.Create }))
                    {
                        tx.Put(db, Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(value));
                        tx.Commit();
                    }
                }
                catch (Exception ex)
                {
                    tx.Abort();

                    throw ex;
                }
            }
        }

        public string Get(string dbName, string key)
        {
            using (var tx = env.BeginTransaction(TransactionBeginFlags.ReadOnly))
            using (var db = tx.OpenDatabase(dbName))
            {
                return Encoding.UTF8.GetString(tx.Get(db, Encoding.UTF8.GetBytes(key)));
            }
        }

        public void Put(string dbname, byte[] key, byte[] value)
        {
            using (var tx = env.BeginTransaction())
            {
                try
                {
                    using (var db = tx.OpenDatabase(dbname, new DatabaseConfiguration { Flags = DatabaseOpenFlags.Create }))
                    {
                        tx.Put(db, key, value);

                        tx.Commit();
                    }
                }
                catch (Exception ex)
                {
                    tx.Abort();

                    throw ex;
                }
            }
        }

        public byte[] Get(string dbName, byte[] key)
        {
            using (var tx = env.BeginTransaction(TransactionBeginFlags.ReadOnly))
            {
                using (var db = tx.OpenDatabase(dbName))
                {
                    return tx.Get(db, key);
                }
            }
        }

        public void Delete(string dbName, byte[] key)
        {
            using (var tx = env.BeginTransaction())
            {
                try
                {
                    using (var db = tx.OpenDatabase(dbName))
                    {
                        tx.Delete(db, key);

                        tx.Commit();
                    }
                }
                catch (Exception ex)
                {
                    tx.Abort();

                    throw ex;
                }
            }
        }

        public IEnumerable<UInt64> GetKeys(string dbName)
        {
            var result = new List<UInt64>();
            using (var tx = env.BeginTransaction(TransactionBeginFlags.ReadOnly))
            using (var db = tx.OpenDatabase(dbName))
            {
                using (var cur = tx.CreateCursor(db))
                {
                    var keybytes = new byte[8];
                    if (cur.MoveToFirst())
                    {
                        do
                        {
                            var keyvaluepair = cur.GetCurrent();
                            var keyint = BitConverter.ToUInt64(keyvaluepair.Key);
                            var key = Encoding.UTF8.GetString(keyvaluepair.Key);
                            var value = Encoding.UTF8.GetString(keyvaluepair.Value);
                            result.Add(keyint);
                            log.Debug($"key={key} keyint={keyint} value={value}");
                        }
                        while (cur.MoveNext());
                    }
                }
            }

            return result;
        }

        public void PutObject(string dbname,string key, object value)
        {
            using (var tx = env.BeginTransaction())
            {
                try
                {
                    using (var db = tx.OpenDatabase(dbname, new DatabaseConfiguration { Flags = DatabaseOpenFlags.Create }))
                    {
                        tx.Put(db, Encoding.UTF8.GetBytes(key), ObjectToByteArray(value));

                        tx.Commit();
                    }
                }
                catch (Exception ex)
                {
                    tx.Abort();

                    throw ex;
                }
            }
        }

        public object GetObject(string dbName, string key)
        {
            using (var tx = env.BeginTransaction(TransactionBeginFlags.ReadOnly))
            using (var db = tx.OpenDatabase(dbName))
            {
                return ByteArrayToObject(tx.Get(db, Encoding.UTF8.GetBytes(key)));
            }
        }
    }
}
