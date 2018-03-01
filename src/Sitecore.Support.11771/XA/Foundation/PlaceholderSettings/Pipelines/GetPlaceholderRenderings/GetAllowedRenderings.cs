using Microsoft.Extensions.DependencyInjection;
using Sitecore.Data;
using Sitecore.Data.Items;
using Sitecore.DependencyInjection;
using Sitecore.Diagnostics;
using Sitecore.Mvc.Pipelines.Response.GetXmlBasedLayoutDefinition;
using Sitecore.Pipelines.GetPlaceholderRenderings;
using Sitecore.XA.Foundation.Multisite.Extensions;
using Sitecore.XA.Foundation.PlaceholderSettings;
using Sitecore.XA.Foundation.SitecoreExtensions.Extensions;
using Sitecore.XA.Foundation.SitecoreExtensions.Interfaces;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Sitecore.Support.XA.Foundation.PlaceholderSettings.Pipelines.GetPlaceholderRenderings
{
  public class GetAllowedRenderings : Sitecore.Pipelines.GetPlaceholderRenderings.GetAllowedRenderings
  {
    protected ILayoutsPageContext LayoutsPageContext { get; set; }

    public GetAllowedRenderings(ILayoutsPageContext layoutsPageContext)
    {
      LayoutsPageContext = layoutsPageContext;
    }

    public new void Process(GetPlaceholderRenderingsArgs args)
    {
      Assert.IsNotNull(args, "args");

      var deviceID = Context.Device.ID;
      if (!ID.IsNullOrEmpty(args.DeviceId))
      {
        deviceID = args.DeviceId;
      }
      var currentItem = Context.Item;
      var contextItemPath = args.CustomData[Sitecore.XA.Foundation.PlaceholderSettings.Constants.ContextItemPath];
      if (currentItem == null && contextItemPath != null)
      {
        currentItem = args.ContentDatabase.GetItem(contextItemPath.ToString());
      }

      if (currentItem != null)
      {
        var phItems = new List<Item>();
        if (currentItem.IsSxaSite())
        {
          string originalPlaceholderKey;
          string placeholderKey = ((originalPlaceholderKey = args.CustomData["placeholderKey"] as string) != null) ? originalPlaceholderKey : args.PlaceholderKey;
          var sxaPlaceholderItems = this.GetSxaPlaceholderItems(args.LayoutDefinition, placeholderKey, currentItem, deviceID);
          if (sxaPlaceholderItems.Any())
          {
            phItems.AddRange(sxaPlaceholderItems);
          }
          else
          {
            var sitePlaceholderItem = this.GetSxaPlaceholderItem(args.PlaceholderKey, currentItem);
            if (sitePlaceholderItem != null)
            {
              phItems.Add(sitePlaceholderItem);
            }
            else
            {
              phItems.Add(GetGlobalPlaceholderItem(args));
            }
          }
        }
        else
        {
          phItems.Add(GetGlobalPlaceholderItem(args));
        }

        if (args.PlaceholderRenderings == null)
        {
          args.PlaceholderRenderings = new List<Item>();
        }
        foreach (var phItem in phItems)
        {
          List<Item> renderings = null;
          if (phItem != null)
          {
            args.HasPlaceholderSettings = true;
            bool allowedControlsSpecified;
            renderings = GetRenderings(phItem, out allowedControlsSpecified);
          }

          if (renderings == null)
          {
            continue;
          }
          args.PlaceholderRenderings.AddRange(renderings);
        }
        if (args.PlaceholderRenderings.Any())
        {
          args.Options.ShowTree = false;
        }
      }
    }

    protected virtual Item GetGlobalPlaceholderItem(GetPlaceholderRenderingsArgs args)
    {
      Item placeholderItem;
      if (ID.IsNullOrEmpty(args.DeviceId))
      {
        placeholderItem = LayoutsPageContext.GetPlaceholderItem(args.PlaceholderKey, args.ContentDatabase, args.LayoutDefinition);
      }
      else
      {
        using (new DeviceSwitcher(args.DeviceId, args.ContentDatabase))
        {
          placeholderItem = LayoutsPageContext.GetPlaceholderItem(args.PlaceholderKey, args.ContentDatabase, args.LayoutDefinition);
        }
      }
      return placeholderItem;
    }

    #region Added code
    private List<Item> GetSxaPlaceholderItems(string layout, string placeholderKey, Item currentItem, ID deviceId)
    {
      GetXmlBasedLayoutDefinitionArgs args = new GetXmlBasedLayoutDefinitionArgs
      {
        ContextItem = currentItem,
        Result = XElement.Parse(layout)
      };
      new Sitecore.Support.XA.Foundation.PlaceholderSettings.Pipelines.GetXmlBasedLayoutDefinition.AddPartialDesignsPlaceholderSettings().Process(args);
      return (from p in (from d in args.Result.Descendants("d")
          where new ID((d.Attribute("id") == null) ? null : d.Attribute("id").Value) == deviceId
          select d).Descendants<XElement>("p")
        where ((p.Attribute("key") == null) ? null : p.Attribute("key").Value) == placeholderKey
        select p.Attribute("md") into attr
        select currentItem.Database.GetItem(attr.Value)).ToList<Item>();
    }

    private Item GetSxaPlaceholderItem(string phKey, Item currentItem)
    {
      Item placeholderSettingsFolder = ServiceProviderServiceExtensions.GetService<IPlaceholderSettingsContext>(ServiceLocator.ServiceProvider).GetPlaceholderSettingsFolder(currentItem);
      if (placeholderSettingsFolder == null)
      {
        return null;
      }
      return (from i in placeholderSettingsFolder.Axes.GetDescendants()
        where i.InheritsFrom(Sitecore.XA.Foundation.PlaceholderSettings.Templates.Placeholder.ID)
        select i).FirstOrDefault<Item>(item => item[Sitecore.XA.Foundation.PlaceholderSettings.Templates.Placeholder.Fields.PlaceholderKey].Equals(phKey));
    }
    #endregion
  }
}