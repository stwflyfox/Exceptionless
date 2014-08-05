﻿using System;
using System.Collections.Generic;
using System.Linq;
using CodeSmith.Core.Component;
using Exceptionless.Core.Extensions;
using Exceptionless.Models.Data;
using Newtonsoft.Json.Linq;

namespace Exceptionless.Core.Plugins.EventUpgrader {
    [Priority(2000)]
    public class V2EventUpgrade : IEventUpgraderPlugin {
        public void Upgrade(EventUpgraderContext ctx) {
            if (ctx.Version > new Version(2, 0))
                return;

            foreach (var doc in ctx.Documents.OfType<JObject>()) {
                bool isNotFound = doc.GetPropertyStringValue("Code") == "404";

                if (ctx.IsMigration) {
                    doc.Rename("ErrorStackId", "StackId");
                } else {
                    if (isNotFound)
                        doc.Remove("Id");
                    else
                        doc.RenameOrRemoveIfNullOrEmpty("Id", "ReferenceId");

                    doc.Remove("OrganizationId");
                    doc.Remove("ProjectId");
                    doc.Remove("ErrorStackId");
                }

                doc.RenameOrRemoveIfNullOrEmpty("OccurrenceDate", "Date");
                doc.Remove("ExceptionlessClientInfo");
                if (!doc.RemoveIfNullOrEmpty("Tags")) {
                    var tags = doc.GetValue("Tags");
                    if (tags.Type == JTokenType.Array) {
                        foreach (JToken tag in tags.Where(tag => tag.ToString().Length > 255))
                            tag.Remove();
                    }
                }

                doc.RenameOrRemoveIfNullOrEmpty("RequestInfo", "req");
                doc.RenameOrRemoveIfNullOrEmpty("EnvironmentInfo", "env");

                var targetNames = new List<string>(new[] { "ExtendedData", "Data", "GenericArguments", "Parameters" });
                var elements = doc.Descendants().OfType<JProperty>().Where(p => targetNames.Contains(p.Name)).ToList();
                elements.RenameAll("ExtendedData", "Data");

                var extendedData = doc.Property("Data") != null ? doc.Property("Data").Value as JObject : null;
                if (extendedData != null)
                    extendedData.RenameOrRemoveIfNullOrEmpty("TraceLog", "trace");

                var error = new JObject();
                error.MoveOrRemoveIfNullOrEmpty(doc, "Code", "Type", "Message", "Inner", "StackTrace", "TargetMethod", "Modules");

                MoveExtraExceptionProperties(error, extendedData);
                var inner = error["inner"] as JObject;
                while (inner != null) {
                    MoveExtraExceptionProperties(inner);
                    inner = inner["inner"] as JObject;
                }

                doc.Add("Type", new JValue(isNotFound ? "404" : "error"));
                doc.Add("err", error);

                string emailAddress = doc.GetPropertyStringValueAndRemove("UserEmail");
                string userDescription = doc.GetPropertyStringValueAndRemove("UserDescription");
                if (!String.IsNullOrWhiteSpace(emailAddress) && !String.IsNullOrWhiteSpace(userDescription))
                    doc.Add("desc", JObject.FromObject(new UserDescription(emailAddress, userDescription)));

                string identity = doc.GetPropertyStringValueAndRemove("UserName");
                if (!String.IsNullOrWhiteSpace(identity))
                    doc.Add("user", JObject.FromObject(new UserInfo(identity)));

                doc.RemoveAllIfNullOrEmpty("Data", "GenericArguments", "Parameters");
            }
        }

        private void MoveExtraExceptionProperties(JObject doc, JObject extendedData = null) {
            if (doc == null)
                return;

            if (extendedData == null)
                extendedData = doc["Data"] as JObject;

            string json = extendedData != null && extendedData["__ExceptionInfo"] != null ? extendedData["__ExceptionInfo"].ToString() : null;
            if (String.IsNullOrEmpty(json))
                return;

            try {
                var extraProperties = JObject.Parse(json);
                foreach (var property in extraProperties.Properties())
                    doc.Add(property.Name, property.Value);
            } catch (Exception) {}

            extendedData.Remove("__ExceptionInfo");
        }
    }
}