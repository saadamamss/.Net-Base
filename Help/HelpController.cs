using DataForge.Common.Models;
using DataForge.Fields;
using DataForge.Fields.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DataForge.Help;


[ApiController]
[Route("api/v1/help")]
[Authorize]
public class HelpController : ControllerBase
{
    private readonly HelpService _helpService;
    public HelpController(HelpService helpService)
    {
        _helpService = helpService;
    }


    [HttpGet("field-types")]
    public ActionResult GetFieldTypes()
    {
        return Ok(ApiResponse<List<FieldTypeDto>>.Ok(_helpService.GetFieldTypes()));
    }
}