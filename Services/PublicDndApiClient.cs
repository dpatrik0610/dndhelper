using dndhelper.Models;
using dndhelper.Services.Interfaces;
using dndhelper.Utils;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

public class PublicDndApiClient : IPublicDndApiClient
{
    private readonly HttpClient _httpClient;

    public PublicDndApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IEnumerable<Equipment>> GetEquipmentListAsync()
    {
        var response = await _httpClient.GetAsync("equipment");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();

        var indexResults = JsonConvert.DeserializeObject<EquipmentListResponse>(json);

        if (indexResults == null || indexResults.Results.IsNullOrEmpty())
            return Enumerable.Empty<Equipment>();

        List<Equipment> result = new List<Equipment>();

        foreach (var item in indexResults.Results)
        {
            var equipment = await GetEquipmentByIndexAsync(item.Index!);
            result.Add(equipment);
        }

        return result;
    }


    public async Task<Equipment?> GetEquipmentByIndexAsync(string index)
    {
        var response = await _httpClient.GetAsync($"equipment/{index}");
        if (!response.IsSuccessStatusCode)
            return null;

        var json = await response.Content.ReadAsStringAsync();
        var equipment = JsonConvert.DeserializeObject<Equipment>(json);
        return equipment;
    }
}

public class EquipmentListResponse
{
    public List<SingleEquipmentResponse> Results { get; set; } = new();
}

public class SingleEquipmentResponse
{
    public string? Index { get; set; }
}
