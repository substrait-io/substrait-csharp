using Substrait.Core;
using Substrait.Protobuf;

namespace Substrait.Relation.Tests
{
  [TestClass()]
  public class ProtoConverterTests
  {
    [TestMethod()]
    public void ToRel_FetchRel_Test()
    {
      FetchRel fetchRel = new FetchRel();
      fetchRel.Count = 1; // limit 1
      var rel = new Protobuf.Rel()
      {
        Fetch = fetchRel
      };

      fetchRel.Input = rel;

      ProtoConverter converter = new();
      var result = converter.ToRel(rel) as Fetch;
      Assert.IsNotNull(result);
      Assert.AreEqual(1, result.Count);
    }
  }
}