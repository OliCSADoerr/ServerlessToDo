using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Extensions.CosmosDB;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Cosmos.Linq;
using System.Text.Json;

namespace api
{
	public static class TodoItems
	{
		private static AuthorizedUser GetCurrentUserName()
		{
			// On localhost claims will be empty
			string name = "Demo User";
			string upn = "dev@localhost";

			//foreach (Claim claim in ClaimsPrincipal.Current.Claims)
			//{
			//	if (claim.Type == "name")
			//	{
			//		name = claim.Value;
			//	}
			//	if (claim.Type == "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/upn")
			//	{
			//		upn = claim.Value;
			//	}
				//Uncomment to print all claims to log output for debugging
				//log.logVerbose("Claim: " + claim.Type + " Value: " + claim.Value);
			//}
			return new AuthorizedUser() {DisplayName = name, UniqueName = upn };
		}
		
		// Add new item
		[FunctionName("TodoItemAdd")]
		public static async Task<HttpResponseMessage> AddItem(
			[HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "todoitem")] HttpRequestMessage req,
			[CosmosDB(databaseName: "ServerlessTodo", containerName: "TodoItems", Connection = "CosmosDBConnectionString")] IAsyncCollector<TodoItem> todoWriter,	ILogger log)
		{
			var newItem = await req.Content.ReadAsAsync<TodoItem>();
			log.LogInformation("Upserting item {ItemName}", newItem.ItemName);
			if (string.IsNullOrEmpty(newItem.id))
			{
				log.LogInformation("Item is new.");
				newItem.id = Guid.NewGuid().ToString();
				newItem.ItemCreateDate = DateTime.UtcNow;
				newItem.ItemOwner = GetCurrentUserName().UniqueName;
			}
			await todoWriter.AddAsync(newItem);

			var response = req.CreateResponse(HttpStatusCode.OK);
			response.Content = new StringContent(JsonSerializer.Serialize(newItem), System.Text.Encoding.UTF8, "application/json");
			return response;
		}

		// Get all items
		[FunctionName("TodoItemGetAll")]
		public static async Task<HttpResponseMessage> GetAll(
		   [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "todoitem")] HttpRequestMessage req,
		   [CosmosDB(databaseName: "ServerlessTodo", containerName: "TodoItems", Connection = "CosmosDBConnectionString")] CosmosClient cosmosClient, 
		   ILogger log)
		{
			var currentUser = GetCurrentUserName();
			log.LogInformation("Getting all Todo items for user: {User}", currentUser.UniqueName);

			var container = cosmosClient.GetContainer("ServerlessTodo", "TodoItems");
			log.LogInformation("Building query to DB ...");
			var iterator = container.GetItemQueryIterator<TodoItem>(new QueryDefinition("SELECT * FROM c WHERE c.ItemOwner = @owner").WithParameter("@owner", currentUser.UniqueName));
						
			var items = new List<TodoItem>();
			while (iterator.HasMoreResults)
			{
				var page = await iterator.ReadNextAsync();
				items.AddRange(page);
			}

			//Debug Information
			var payloadJson = JsonSerializer.Serialize(items);
			log.LogInformation("Fetched {Count} todo items: {ItemsJson}", items.Count, payloadJson);
			try
			{
				var return_values = new { UserName = currentUser.DisplayName, Items = items };
				log.LogInformation("Try section to return values");
				var response = req.CreateResponse(HttpStatusCode.OK);
				response.Content = new StringContent(JsonSerializer.Serialize(return_values), System.Text.Encoding.UTF8, "application/json");
				return response;
			}
			catch (Exception ex)
			{
				log.LogError("Error message: {ExceptionMessage}", ex.Message);
				return req.CreateResponse(HttpStatusCode.BadGateway);
			}
		}

		// Delete item by id
		[FunctionName("TodoItemDelete")]
		public static async Task<HttpResponseMessage> DeleteItem(
		   [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "todoitem/{id}")] HttpRequestMessage req,
		   [CosmosDB(databaseName: "ServerlessTodo", containerName: "TodoItems", Connection = "CosmosDBConnectionString")] CosmosClient cosmosClient, string id,
		   ILogger log)
		{
			var currentUser = GetCurrentUserName();
			log.LogInformation("Deleting document with ID {Id} for user {User}", id, currentUser.UniqueName);
			var container = cosmosClient.GetContainer("ServerlessTodo", "TodoItems");
			await container.DeleteItemAsync<TodoItem>(id, new PartitionKey(currentUser.UniqueName));
			log.LogInformation("Entry successfully deleted");
			return req.CreateResponse(HttpStatusCode.NoContent);
		}
	}
}