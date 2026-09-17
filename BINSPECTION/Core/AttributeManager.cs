using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace BINSPECTION.Core
{
    public class AttributeManager
    {
        private const string AttributeName = "BINSPECTION";

        private const string NumberParameter = "CharacteristicNumber";

        private AttributeDef _attributeDef;

        public void CreateAttributeDefinition(ISldWorks swApp)
        {
            _attributeDef =
                (AttributeDef)swApp.DefineAttribute(AttributeName);

            _attributeDef.AddParameter(
                NumberParameter,
                (int)swParamType_e.swParamTypeDouble,
                0,
                0);

            _attributeDef.Register();
        }

        // AttachToDimension()
        // ReadFromDimension()
        // AttachToBalloon()
        // ReadFromBalloon()
    }
}