using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;

using LightningDB;

namespace tunlim.api
{
    public class Lightning
    {
        protected LightningEnvironment env;

        public Lightning(string envPath, int numOfDb)
        {
            env = new LightningEnvironment(envPath);
            env.MaxDatabases = numOfDb;
            env.Open();
        }

        ~Lightning()
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
            using (var db = tx.OpenDatabase(dbname, new DatabaseConfiguration { Flags = DatabaseOpenFlags.Create }))
            {
                tx.Put(db, Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(value));
                tx.Commit();
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
            using (var db = tx.OpenDatabase(dbname, new DatabaseConfiguration { Flags = DatabaseOpenFlags.Create }))
            {
                tx.Put(db, key, value);
                tx.Commit();
            }
        }

        public byte[] Get(string dbName, byte[] key)
        {
            using (var tx = env.BeginTransaction(TransactionBeginFlags.ReadOnly))
            using (var db = tx.OpenDatabase(dbName))
            {
                return tx.Get(db, key);
            }
        }

        public IEnumerable<string> GetKeys(string dbName)
        {
            var result = new List<string>();
            using (var tx = env.BeginTransaction(TransactionBeginFlags.ReadOnly))
            using (var db = tx.OpenDatabase(dbName))
            {
                using (var cur = tx.CreateCursor(db))
                {
                    var keybytes = new byte[8];
                    if (cur.MoveToFirst())
                    {
                        var keyvaluepair = cur.GetCurrent();
                        result.Add(Encoding.ASCII.GetString(keyvaluepair.Key));
                        while (cur.MoveNext())
                        {
                            var kvp = cur.GetCurrent();
                            result.Add(Encoding.ASCII.GetString(kvp.Key));
                        }
                    }
                }
            }

            return result;
        }

        public void PutObject(string dbname,string key, object value)
        {
            using (var tx = env.BeginTransaction())
            using (var db = tx.OpenDatabase(dbname, new DatabaseConfiguration { Flags = DatabaseOpenFlags.Create }))
            {
                tx.Put(db, Encoding.UTF8.GetBytes(key), ObjectToByteArray(value));
                tx.Commit();
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
