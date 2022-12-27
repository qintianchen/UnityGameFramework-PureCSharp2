using System;

namespace Game.Common
{
    public class SingleTon<T> where T : class, new()
    {
        private static Lazy<T> lazy = new(() => new T());
        public static T Instance => lazy.Value;
    }
}