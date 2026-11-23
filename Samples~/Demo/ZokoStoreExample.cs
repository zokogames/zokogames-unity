using UnityEngine;
using ZokoGames;

/// <summary>
/// Minimal purchase flow. Attach to any GameObject. In the Editor the simulator answers with a three-item test catalogue,
/// so this runs without a browser; in a WebGL build the portal's real checkout opens.
/// </summary>
public class ZokoStoreExample : MonoBehaviour
{
    async void Start()
    {
        await ZokoSDK.InitAsync();
        if (!ZokoSDK.Store.IsAvailable) return;           // hide your shop button on hosts without purchases

        try
        {
            var catalog = await ZokoSDK.Store.GetCatalogItemsAsync(limit: 20);
            foreach (var item in catalog)
                Debug.Log(item.Sku + "  " + item.Name + "  " + (item.Price != null ? item.Price.Amount + " " + item.Price.Currency : "-"));
            if (catalog.Count == 0) return;

            string bought = await ZokoSDK.Store.BuyItemAsync(catalog[0].Sku);   // opens the checkout; verify ownership on your server
            Debug.Log("paid for " + bought);

            if (catalog[0].Consumable) await ZokoSDK.Store.ConsumeItemAsync(catalog[0].Sku, 1);

            string token = await ZokoSDK.Store.GetPurchaseTokenAsync();      // send to your server, which verifies what the player owns
            Debug.Log("purchase token: " + token);

            foreach (var owned in await ZokoSDK.Store.GetPlayerInventoryAsync())
                Debug.Log(owned.Sku + " x" + (owned.Quantity.HasValue ? owned.Quantity.Value.ToString() : "?"));
        }
        catch (ZokoException e)
        {
            if (e.Error.Is(ZokoErrorCodes.Store.PurchaseCancelled)) Debug.Log("player closed the checkout");
            else if (e.Error.Is(ZokoErrorCodes.Store.UserNotAuthenticated)) ZokoSDK.User.ShowAuthPrompt((u, err) => { });
            else Debug.LogWarning(e.Error);
        }
    }
}
