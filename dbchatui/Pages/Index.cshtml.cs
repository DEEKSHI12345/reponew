using Azure;
using Azure.AI.OpenAI;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using static AZURE_AI.DataService;

namespace AZURE_AI.Pages
{
    public class IndexModel : PageModel
    {
        [BindProperty]
        public string UserPrompt { get; set; } = string.Empty;

        [BindProperty]
        public List<string> SelectedColumns { get; set; } = new List<string>();

        public List<List<string>> Data { get; set; } = new List<List<string>>();
        public string Summary { get; set; } = string.Empty;

        [BindProperty]
        public string Query { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;

        [BindProperty]
        public string LastGeneratedQuery { get; set; } = string.Empty;

        [BindProperty]
        public string SortBy { get; set; } = string.Empty;
        [BindProperty]
        public string FilterBy { get; set; } = string.Empty;

        [BindProperty]
        public string OrderBy { get; set; } = "ASC";
        [BindProperty]
        public string TableBy { get; set; } = string.Empty;
        [BindProperty]
        public string ColumnBy { get; set; } = string.Empty;

        [BindProperty]
        public string ValueBy { get; set; } = string.Empty;

        [BindProperty]
        public string UserQuery { get; set; }

        public List<TableColumns> TableColumns { get; set; } = new List<TableColumns>();
        public List<TableSchema> DatabaseSchema { get; set; } = new List<TableSchema>();
        public string Source { get; set; } = "Projects";
        public List<string> Foreign { get; set; }

        public void OnGet()
        {
            DatabaseSchema = DataService.GetDatabaseSchema();
            if (!string.IsNullOrEmpty(UserQuery))
            {
                TableColumns = DataService.ExtractUsedTablesAndColumns(UserQuery);
            }
            else
            {
                TableColumns = new List<TableColumns>(); // Initialize as needed
            }
        }

        public IActionResult OnGetFilterValues(string tableName, string columnName)
        {
            if (string.IsNullOrEmpty(tableName) || string.IsNullOrEmpty(columnName))
            {
                return new JsonResult(new { success = false });
            }

            var filterValues = DataService.GetDistinctColumnValues(tableName, columnName);
            return new JsonResult(new { success = true, filterValues });
        }


        public void OnPost()
        {
            LoadDatabaseSchema();
            RunQuery(UserPrompt);
            List<ForeignKeyRelationship> relationships = DataService.GetForeignKeyRelationships(ExtractSourceTableName(Query));
            List<string> targetTables = relationships.Select(r => r.TargetTable).Distinct().ToList();
            Foreign = targetTables;
        }

        public IActionResult OnGetApplySorting(string sortColumn,string sortOrder)
        {
            LoadDatabaseSchema();

            if (!string.IsNullOrEmpty(LastGeneratedQuery))
            {
                Query = LastGeneratedQuery;


                if (!string.IsNullOrEmpty(SortBy))
                {
                    Query = DataService.ApplySorting(Query, SortBy, OrderBy);
                }


                Data = DataService.GetTable(Query);

                var parsedResult = DataService.ParseQuery(Query);
                TableColumns = parsedResult.GroupBy(pr => pr.Table).Select(g => new TableColumns
                {
                    Table = g.Key,
                    Columns = g.SelectMany(pr => pr.Columns).Distinct().Select(c => c.Contains('.') ? c.Split('.').Last() : c).ToList()
                }).ToList();

                LastGeneratedQuery = Query;
                List<ForeignKeyRelationship> relationships = DataService.GetForeignKeyRelationships(ExtractSourceTableName(Query));
                List<string> targetTables = relationships.Select(r => r.TargetTable).Distinct().ToList();
                Foreign = targetTables;
            }

            return Page();
        }

        public IActionResult OnPostApplyFiltering(string tableName,string columnName,string value)
        {
            LoadDatabaseSchema();

            if (!string.IsNullOrEmpty(LastGeneratedQuery))
            {
                Query = LastGeneratedQuery;


                if (!string.IsNullOrEmpty(TableBy))
                {
                    Query = DataService.ApplyFiltering(Query, TableBy, ColumnBy,ValueBy);
                }


                Data = DataService.GetTable(Query);

                var parsedResult = DataService.ParseQuery(Query);
                TableColumns = parsedResult.GroupBy(pr => pr.Table).Select(g => new TableColumns
                {
                    Table = g.Key,
                    Columns = g.SelectMany(pr => pr.Columns).Distinct().Select(c => c.Contains('.') ? c.Split('.').Last() : c).ToList()
                }).ToList();

                LastGeneratedQuery = Query;
                List<ForeignKeyRelationship> relationships = DataService.GetForeignKeyRelationships(ExtractSourceTableName(Query));
                List<string> targetTables = relationships.Select(r => r.TargetTable).Distinct().ToList();
                Foreign = targetTables;
            }

            return Page();
        }


        public async Task<JsonResult> OnGetFilterColumnsAsync(string table)
        {
            if (string.IsNullOrEmpty(table))
            {
                return new JsonResult(new { success = false, message = "Table name cannot be empty." });
            }

            try
            {
                var columns = await DataService.GetColumnsForTableAsync(table);
                return new JsonResult(new { success = true, columns });
            }
            catch (Exception ex)
            {
                return new JsonResult(new { success = false, message = ex.Message });
            }
        }




        private void LoadDatabaseSchema()
        {
            var schema = DataService.GetDatabaseSchema();
            DatabaseSchema = schema.Select(s => new TableSchema
            {
                TableName = s.TableName,
                Columns = s.Columns
            }).ToList();
        }

        public void RunQuery(string prompt)
        {
            string OpenAIEndpoint = "https://magicvilla123.openai.azure.com/";
            string OpenAIKey = "1fcd028d8e744609be7ded7d8e4d63d2";
            string deploymentName = "magicvilla";

            OpenAIClient openAIClient = new(new Uri(OpenAIEndpoint), new AzureKeyCredential(OpenAIKey));
            string systemMessage = @"
your are a helpful, cheerful database assistant. 
use the following database schema when creating your answers:

- Projects (ProjectId,ProjectName,StartDate,EndDate,Budget,ProjectManagerId,StatusId,CreatedDate,UpdatedDate,CreatedBy,UpdatedBy,IsActive)
            - Members (MemberId,MemberName,Email,Contact,CreatedDate,UpdatedDate,CreatedBy,UpdatedBy,Password,IsActive)
            - Comments (CommentId,CommenterId,PostedOn,Comment,TaskId,ProjectId,Reply)
            - ProjectMembers (ProjectMemberId,ProjectId,MemberId,IsActive)
            - Roles (RoleId,Role)
            -Statuses (StatusId,Status)
            - TaskMembers (Id,TaskId,MemberId,IsActive)
            - TASKS (TaskId,TaskName,TaskDetails,ProjectId,StatusId,CreatedDate,UpdatedDate,CreatedBy,UpdatedBy,IsActive)
            - UserRefreshTokens (Email,RefreshToken,Id,CreatedDate,CreatedBy,UpdatedDate,UpdatedBy)

include column name headers in the query results.

always provide your answer in the json format below:
{ ""summary"": ""your-summary"", ""query"":  ""your-query"" }
output only json.
in the preceding json response, substitute ""your-query"" with microsoft sql server query to retrieve the requested data.
in the preceding json response, substitute ""your-summary"" with a summary of the query.
always include all columns in the table.
if the resulting query is non-executable, replace ""your-query"" with na, but still substitute ""your-query"" with a summary of the query.
";

            if (string.IsNullOrWhiteSpace(systemMessage) || string.IsNullOrWhiteSpace(prompt))
            {
                Error = "System message or user prompt cannot be empty.";
                return;
            }

            ChatCompletionsOptions chatCompletionsOptions = new ChatCompletionsOptions()
            {
                Messages = {
                    new ChatRequestSystemMessage(systemMessage),
                    new ChatRequestUserMessage(prompt)
                },
                DeploymentName = deploymentName
            };

            try
            {
                ChatCompletions chatCompletionsResponse = openAIClient.GetChatCompletions(chatCompletionsOptions);

                var response = JsonSerializer.Deserialize<AIQuery>(chatCompletionsResponse.Choices[0].Message.Content
                    .Replace("```json", "").Replace("```", ""));

                Summary = response?.summary ?? string.Empty;
                Query = response?.query ?? string.Empty;

                if (SelectedColumns.Any())
                {
                    var tables = new HashSet<string>(SelectedColumns.Select(col => col.Split('.')[0]));
                    foreach (var table in tables)
                    {
                        List<JoinCondition> joinConditions = DataService.GetJoinConditions(tables.ToList());
                        Query = DataService.UpdateQueryWithSelectedColumns(Query, SelectedColumns, joinConditions);
                    }
                }

                if (!string.IsNullOrEmpty(SortBy))
                {
                    Query = DataService.ApplySorting(Query, SortBy, OrderBy);
                }

                string sourceTableName = ExtractSourceTableName(Query);

                // Retrieve the corresponding columns for the source table
                var sourceTableSchema = DatabaseSchema.FirstOrDefault(table => table.TableName.Equals(sourceTableName, StringComparison.OrdinalIgnoreCase));

                if (Query.Contains("SELECT *") && sourceTableSchema != null)
                {
                    var columns = string.Join(", ", sourceTableSchema.Columns);
                    Query = Query.Replace("SELECT *", $"SELECT {columns}");
                }

                TableColumns = DataService.ExtractUsedTablesAndColumns(Query);
                Data = DataService.GetTable(Query);

                var parsedResult = DataService.ParseQuery(Query);
                TableColumns = parsedResult.GroupBy(pr => pr.Table).Select(g => new TableColumns
                {
                    Table = g.Key,
                    Columns = g.SelectMany(pr => pr.Columns)
                               .Distinct()
                               .Select(c => c.Contains('.') ? c.Split('.').Last() : c)
                               .ToList()
                }).ToList();
                LastGeneratedQuery = Query;
            }
            catch (Exception e)
            {
                Error = e.Message;
            }
        }

        public IActionResult OnPostUpdateQuery()
        {
            if (!string.IsNullOrEmpty(LastGeneratedQuery))
            {
                Query = LastGeneratedQuery;
            }

            if (!string.IsNullOrEmpty(Query))
            {
                if (SelectedColumns.Any())
                {
                    var tables = new HashSet<string>(SelectedColumns.Select(col => col.Split('.')[0]));

                    // Get join conditions for all tables involved
                    List<JoinCondition> joinConditions = DataService.GetJoinConditions(tables.ToList());

                    // Update the query with selected columns and join conditions
                    Query = DataService.UpdateQueryWithSelectedColumns(Query, SelectedColumns, joinConditions);
                }

                string sourceTableName = ExtractSourceTableName(Query);
                var sourceTableSchema = DatabaseSchema.FirstOrDefault(table => table.TableName.Equals(sourceTableName, StringComparison.OrdinalIgnoreCase));

                if (Query.Contains("SELECT *") && sourceTableSchema != null)
                {
                    var columns = string.Join(", ", sourceTableSchema.Columns);
                    Query = Query.Replace("SELECT *", $"SELECT {columns}");
                }

                // Apply sorting if SortBy is not empty
                if (!string.IsNullOrEmpty(SortBy))
                {
                    Query = DataService.ApplySorting(Query, SortBy, OrderBy);
                }

                Data = DataService.GetTable(Query);

                var parsedResult = DataService.ParseQuery(Query);
                TableColumns = parsedResult.GroupBy(pr => pr.Table).Select(g => new TableColumns
                {
                    Table = g.Key,
                    Columns = g.SelectMany(pr => pr.Columns)
                               .Distinct()
                               .Select(c => c.Contains('.') ? c.Split('.').Last() : c)
                               .ToList()
                }).ToList();
                LastGeneratedQuery = Query;
            }

            return Page();
        }



        private static string ExtractSourceTableName(string query)
        {
            var fromIndex = query.IndexOf("FROM", StringComparison.OrdinalIgnoreCase);
            var whereIndex = query.IndexOf("WHERE", StringComparison.OrdinalIgnoreCase);
            var tableName = query.Substring(fromIndex + 5, (whereIndex == -1 ? query.Length : whereIndex) - fromIndex - 5).Trim();
            return tableName;
        }
    }

    public class AIQuery
    {
        public string summary { get; set; }
        public string query { get; set; }
    }
}
