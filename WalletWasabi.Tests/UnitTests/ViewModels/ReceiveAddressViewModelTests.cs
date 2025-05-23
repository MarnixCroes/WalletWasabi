using System.Collections.Generic;
using System.ComponentModel;
using System.Reactive.Linq;
using Moq;
using NBitcoin;
using WalletWasabi.Fluent.Models.Transactions;
using WalletWasabi.Fluent.Models.UI;
using WalletWasabi.Fluent.Models.Wallets;
using WalletWasabi.Fluent.ViewModels.Wallets.Labels;
using WalletWasabi.Fluent.ViewModels.Wallets.Receive;
using WalletWasabi.Tests.UnitTests.ViewModels.TestDoubles;
using WalletWasabi.Wallets;
using Xunit;
using ScriptType = WalletWasabi.Fluent.Models.Wallets.ScriptType;

namespace WalletWasabi.Tests.UnitTests.ViewModels;

public class ReceiveAddressViewModelTests
{
	[Fact]
	public void CopyCommandShouldSetAddressInClipboard()
	{
		var clipboard = Mock.Of<IUiClipboard>(MockBehavior.Loose);
		var context = new UiContextBuilder().WithClipboard(clipboard).Build();
		var sut = new ReceiveAddressViewModel(context, new TestWallet(), new TestAddress("SomeAddress", ScriptType.SegWit), false);

		sut.CopyAddressCommand.Execute(null);

		var mock = Mock.Get(clipboard);
		mock.Verify(x => x.SetTextAsync("SomeAddress"));
	}

	[Fact]
	public void AutoCopyEnabledShouldCopyToClipboard()
	{
		var clipboard = Mock.Of<IUiClipboard>(MockBehavior.Loose);
		var context = new UiContextBuilder().WithClipboard(clipboard).Build();
		new ReceiveAddressViewModel(context, new TestWallet(), new TestAddress("SomeAddress", ScriptType.SegWit), true);
		var mock = Mock.Get(clipboard);
		mock.Verify(x => x.SetTextAsync("SomeAddress"));
	}

	private class TestWallet : IWalletModel
	{
		public event PropertyChangedEventHandler? PropertyChanged;

		public IObservable<bool> IsCoinjoinRunning { get; } = Observable.Return(true);
		public IObservable<bool> IsCoinjoinStarted { get; } = Observable.Return(true);
		public bool IsCoinJoinEnabled { get; } = true;
		public IAddressesModel Addresses => throw new NotSupportedException();

		public WalletWasabi.Wallets.Wallet Wallet => throw new NotSupportedException();

		public WalletId Id => throw new NotSupportedException();
		public IEnumerable<ScriptPubKeyType> AvailableScriptPubKeyTypes => throw new NotSupportedException();
		public bool SeveralReceivingScriptTypes { get; }

		public string Name
		{
			get => throw new NotSupportedException();
			set => throw new NotSupportedException();
		}

		public IObservable<WalletState> State => throw new NotSupportedException();
		bool IWalletModel.IsHardwareWallet => false;
		public bool IsWatchOnlyWallet => throw new NotSupportedException();
		public IWalletAuthModel Auth => throw new NotSupportedException();
		public IWalletLoadWorkflow Loader => throw new NotSupportedException();
		public IWalletSettingsModel Settings => throw new NotSupportedException();
		public IObservable<bool> HasBalance => throw new NotSupportedException();
		public IWalletPrivacyModel Privacy => throw new NotSupportedException();
		public IWalletCoinjoinModel Coinjoin => throw new NotSupportedException();
		public IObservable<Amount> Balances => throw new NotSupportedException();
		IWalletCoinsModel IWalletModel.Coins => throw new NotSupportedException();
		public Network Network => throw new NotSupportedException();
		IWalletTransactionsModel IWalletModel.Transactions => throw new NotSupportedException();
		public IAmountProvider AmountProvider => throw new NotSupportedException();

		public bool IsLoggedIn { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

		public bool IsLoaded { get; set; }

		public bool IsSelected { get; set; }

		public void Rename(string newWalletName) => throw new NotSupportedException();

		public IEnumerable<(string Label, int Score)> GetMostUsedLabels(Intent intent)
		{
			throw new NotSupportedException();
		}

		public IWalletInfoModel GetWalletInfo()
		{
			throw new NotSupportedException();
		}

		public IWalletStatsModel GetWalletStats()
		{
			throw new NotImplementedException();
		}

		public IPrivacySuggestionsModel GetPrivacySuggestionsModel(SendFlowModel sendParameters)
		{
			throw new NotImplementedException();
		}
	}
}
