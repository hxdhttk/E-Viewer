﻿using ExClient;
using ExViewer.Database;
using ExViewer.Settings;
using ExViewer.Views;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Foundation;
using static System.Runtime.InteropServices.WindowsRuntime.AsyncInfo;
using System.Linq;
using Opportunity.MvvmUniverse.Commands;
using ExClient.Search;
using ExClient.Galleries;
using Opportunity.MvvmUniverse.Collections;

namespace ExViewer.ViewModels
{
    public sealed class SearchVM : SearchResultVM<CategorySearchResult>
    {
        public static IAsyncAction InitAsync()
        {
            var defaultVM = GetVM(string.Empty);
            return defaultVM.SearchResult.LoadMoreItemsAsync(40).AsTask().AsAsyncAction();
        }

        private static AutoFillCacheStorage<string, SearchVM> Cache = AutoFillCacheStorage.Create((string query) =>
        {
            var search = default(CategorySearchResult);
            if (string.IsNullOrEmpty(query))
            {
                var keyword = SettingCollection.Current.DefaultSearchString;
                var category = SettingCollection.Current.DefaultSearchCategory;
                search = Client.Current.Search(keyword, category);
            }
            else
            {
                var uri = new Uri(query);

                var handle = ExClient.Launch.UriLauncher.HandleAsync(uri);
                search = (CategorySearchResult)((ExClient.Launch.SearchLaunchResult)handle.GetResults()).Data;
            }
            var vm = new SearchVM(search);
            AddHistory(vm.Keyword);
            return vm;
        }, 10);

        public static SearchVM GetVM(string query) => Cache.GetOrCreateAsync(query ?? string.Empty).GetResults();

        public static SearchVM GetVM(CategorySearchResult searchResult)
        {
            var vm = new SearchVM(searchResult ?? throw new ArgumentNullException(nameof(searchResult)));
            AddHistory(vm.Keyword);
            Cache[vm.SearchQuery] = vm;
            return vm;
        }

        private SearchVM(CategorySearchResult searchResult)
            : base(searchResult) { }

        public override Command<string> Search { get; } = Command.Create<string>(async (sender, queryText) =>
        {
            var that = (SearchVM)sender.Tag;
            if (SettingCollection.Current.SaveLastSearch)
            {
                SettingCollection.Current.DefaultSearchCategory = that.category;
                SettingCollection.Current.DefaultSearchString = queryText;
            }
            var vm = GetVM(Client.Current.Search(queryText, that.category, that.advancedSearch));
            await RootControl.RootController.Navigator.NavigateAsync(typeof(SearchPage), vm.SearchQuery);
        });

        public override void SetQueryWithSearchResult()
        {
            base.SetQueryWithSearchResult();
            this.Category = this.SearchResult.Category;
            this.AdvancedSearch = (this.SearchResult as AdvancedSearchResult)?.AdvancedSearch ?? new AdvancedSearchOptions();
            this.FileSearch = this.SearchResult as FileSearchResult;
        }

        private Category category;

        public Category Category
        {
            get => this.category;
            set => Set(ref this.category, value);
        }

        private AdvancedSearchOptions advancedSearch;
        public AdvancedSearchOptions AdvancedSearch
        {
            get => this.advancedSearch;
            private set => Set(ref this.advancedSearch, value);
        }

        private FileSearchResult fileSearch;
        public FileSearchResult FileSearch
        {
            get => this.fileSearch;
            private set => Set(ref this.fileSearch, value);
        }
    }
}
