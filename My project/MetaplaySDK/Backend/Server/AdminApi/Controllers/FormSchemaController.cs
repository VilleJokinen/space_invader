// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Akka.Actor;
using Metaplay.Core.Forms;
using Metaplay.Server.Forms;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Metaplay.Server.AdminApi.Controllers
{
    public class FormSchemaController : MetaplayAdminApiController
    {
        readonly IMetaFormSchemaProvider   _schemaProvider;
        readonly IMetaFormValidationEngine _validationEngine;

        public FormSchemaController(ILogger<FormSchemaController> logger, IActorRef adminApi, IMetaFormSchemaProvider schemaProvider,
            IMetaFormValidationEngine validationEngine) : base(logger, adminApi)
        {
            _schemaProvider   = schemaProvider;
            _validationEngine = validationEngine;
        }

        /// <summary>
        /// API endpoint to return detailed information about a single type
        /// Usage:  GET /api/forms/schema/{typeName}
        /// Test:   curl http://localhost:5550/api/forms/schema/{TYPENAME}
        /// </summary>
        [HttpGet("forms/schema/{typeName}")]
        [RequirePermission(MetaplayPermissions.ApiSchemaView)]
        public ActionResult GetSchema(string typeName)
        {
            if (_schemaProvider.TryGetJsonSchema(typeName, out JObject schema))
                return new JsonResult(schema);

            return StatusCode(StatusCodes.Status404NotFound);
        }

        /// <summary>
        /// API endpoint to validate an object of a single type
        /// Usage:  POST /api/forms/schema/{typeName}/validate
        /// Test:   curl http://localhost:5550/api/forms/schema/{TYPENAME}/validate
        /// </summary>
        [HttpPost("forms/schema/{typeName}/validate")]
        [RequirePermission(MetaplayPermissions.ApiSchemaValidate)]
        public async Task<ActionResult> Validate(string typeName)
        {
            if (!MetaFormTypeRegistry.Instance.TryGetByName(typeName, out MetaFormContentType typeSpec))
                return StatusCode(StatusCodes.Status400BadRequest);

            try
            {
                object                            body    = await ParseBodyAsync(typeSpec.Type);
                ICollection<FormValidationResult> results = _validationEngine.ValidateObject(body, typeSpec.Type);
                return new JsonResult(results);
            }
            catch (Exception ex)
            {
                _logger.LogError("Form validation triggered an exception: {0}", ex);
                return new JsonResult(new[] {new FormValidationResult("", "Validation failed due to error.")});
            }
        }

        /// <summary>
        /// API endpoint to return all types
        /// Usage:  GET /api/forms/schema/
        /// Test:   curl http://localhost:5550/api/forms/schema/
        /// </summary>
        [HttpGet("forms/schema")]
        [RequirePermission(MetaplayPermissions.ApiSchemaView)]
        public ActionResult Get()
        {
            return new JsonResult(_schemaProvider.GetAllSchemas());
        }
    }
}
