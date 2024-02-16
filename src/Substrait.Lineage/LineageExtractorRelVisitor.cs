using Substrait.Core;
using Substrait.Relation;
using ProtoRel = Substrait.Protobuf.Rel;

namespace Substrait.Lineage
{
  /// <summary>
  /// A concrete implementation of <see cref="SubstraitRelVisitor"/> for processing Substrait relations and extracting
  /// lineage information.
  /// </summary>
  public class LineageExtractorRelVisitor : SubstraitRelVisitor
  {
    public void Execute(ProtoRel protoRel)
    {
      ProtoConverter converter = new();
      var rel = converter.ToRel(protoRel);
      rel.Accept(this);
    }
  }
}
