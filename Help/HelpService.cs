using DataForge.Fields;
using DataForge.Fields.DTOs;

namespace DataForge.Help;

public class HelpService
{
    public List<FieldTypeDto> GetFieldTypes()
    {
        return Enum.GetValues<FieldType>()
            .Select(f => new FieldTypeDto
            {
                Id = (int)f,
                Value = f.ToString(),
                Label = FieldTypeSchema.GetDefinition(f).Label,
                DbType = FieldTypeSchema.GetDefinition(f).DbType,
                FormComponent = FieldTypeSchema.GetDefinition(f).FormComponent,
                InputType = FieldTypeSchema.GetDefinition(f).InputType,
            })
            .ToList();
    }
}
