
namespace Prague.Core.TypeSystem;

public interface ICResult {
}

public struct CResult<T> : ICResult {
}

public struct CResult<TP, T> where TP : struct, ICResult {}


