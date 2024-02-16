namespace Substrait.Relation
{
  /// <summary>
  /// The FETCH relational operator representing LIMIT or TOP semantics, <see cref="Protobuf.FetchRel"/>
  /// </summary>
  public class Fetch : Rel
  {
    public long Count { get; init; }
  }
}
