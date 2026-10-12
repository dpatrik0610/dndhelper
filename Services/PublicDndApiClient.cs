using dndhelper.Models;
using dndhelper.Services.Interfaces;
using dndhelper.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
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

    public async Task<List<(string Index, string Name)>> GetMonsterIndexAsync(string ruleset)
    {
        var json = JObject.Parse(await _httpClient.GetStringAsync($"{ruleset}/monsters"));
        return (json["results"] as JArray ?? new JArray())
            .Select(r => ((string)r["index"]!, (string)r["name"]!))
            .ToList();
    }

    public async Task<JObject?> GetMonsterAsync(string ruleset, string index)
    {
        var response = await _httpClient.GetAsync($"{ruleset}/monsters/{Uri.EscapeDataString(index)}");
        if (!response.IsSuccessStatusCode) return null;
        return JObject.Parse(await response.Content.ReadAsStringAsync());
    }

    public async Task<byte[]?> GetImageAsync(string path)
    {
        // Only follow paths on the API's own host: the path comes from the API response.
        var url = new Uri(_httpClient.BaseAddress!, path);
        if (url.Host != _httpClient.BaseAddress!.Host) return null;

        var response = await _httpClient.GetAsync(url);
        return response.IsSuccessStatusCode ? await response.Content.ReadAsByteArrayAsync() : null;
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
