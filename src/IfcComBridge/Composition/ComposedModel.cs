using Xbim.Ifc;

namespace IfcComBridge.Composition
{
	/// <summary>A loaded or composed model and its group id (the layout group_id, or the file path for LoadIfc).</summary>
	/// <remarks>
	/// Not IDisposable: its only owner, ComRuntime, disposes every model explicitly when it replaces or
	/// releases the list.
	/// </remarks>
	internal class ComposedModel
	{
		public IfcStore IfcStore { get; private set; }
		public string GroupId {get;set;}

		public ComposedModel(IfcStore store, string groupId) {
			this.IfcStore = store;
			this.GroupId = groupId;
		}

		public void Dispose()
		{
			this.IfcStore.Dispose();
		}
	}
}
