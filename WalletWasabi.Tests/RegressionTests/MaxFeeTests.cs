using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using NBitcoin;
using NBitcoin.Policy;
using NBitcoin.Protocol;
using WalletWasabi.BitcoinRpc;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.Blockchain.TransactionBuilding;
using WalletWasabi.Blockchain.TransactionOutputs;
using WalletWasabi.Exceptions;
using WalletWasabi.FeeRateEstimation;
using WalletWasabi.Helpers;
using WalletWasabi.Models;
using WalletWasabi.Services;
using WalletWasabi.Stores;
using WalletWasabi.Tests.Helpers;
using WalletWasabi.Tests.XunitConfiguration;
using WalletWasabi.Wallets;
using WalletWasabi.WebClients.Wasabi;
using Xunit;

namespace WalletWasabi.Tests.RegressionTests;

/// <seealso cref="RegTestCollectionDefinition"/>
[Collection("RegTest collection")]
public class MaxFeeTests : IClassFixture<RegTestFixture>
{
	public MaxFeeTests(RegTestFixture regTestFixture)
	{
		RegTestFixture = regTestFixture;
	}

	private RegTestFixture RegTestFixture { get; }

	[Fact]
	public async Task CalculateMaxFeeTestAsync()
	{
		await using RegTestSetup setup = await RegTestSetup.InitializeTestEnvironmentAsync(RegTestFixture);
		IRPCClient rpc = setup.RpcClient;
		Network network = setup.Network;
		BitcoinStore bitcoinStore = setup.BitcoinStore;
		ServiceConfiguration serviceConfiguration = setup.ServiceConfiguration;
		string password = setup.Password;

		bitcoinStore.IndexStore.NewFilters += setup.Wallet_NewFiltersProcessed;

		// Create the services.
		// 1. Create connection service.
		NodesGroup nodes = new(setup.Network, requirements: Constants.NodeRequirements);
		nodes.ConnectedNodes.Add(await RegTestFixture.IndexerRegTestNode.CreateNewP2pNodeAsync());

		// 2. Create mempool service.

		Node node = await RegTestFixture.IndexerRegTestNode.CreateNewP2pNodeAsync();
		node.Behaviors.Add(bitcoinStore.CreateUntrustedP2pBehavior());

		// 3. Create wasabi synchronizer service.
		var httpClientFactory = RegTestFixture.IndexerHttpClientFactory;
		var filterProvider = new WebApiFilterProvider(10_000, httpClientFactory, setup.EventBus);
		using Synchronizer synchronizer = new(period: TimeSpan.FromSeconds(3), filterProvider, bitcoinStore, setup.EventBus);
		using FeeRateEstimationUpdater feeProvider = new (TimeSpan.Zero, FeeRateProviders.BlockstreamAsync(new HttpClientFactory()), setup.EventBus);

		// 4. Create key manager service.
		var keyManager = KeyManager.CreateNew(out _, password, network);

		// 5. Create wallet service.
		var workDir = Common.GetWorkDir();

		using MemoryCache cache = BitcoinFactory.CreateMemoryCache();

		var blockProvider = BlockProviders.P2pBlockProvider(new P2PNodesManager(Network.Main, nodes));

		WalletFactory walletFactory = new(network, bitcoinStore, serviceConfiguration, feeProvider, blockProvider, setup.EventBus);
		WalletManager walletManager = new(network, workDir, new WalletDirectories(network, workDir), walletFactory);
		walletManager.Initialize();

		// Get some money, make it confirm.
		var txId = await rpc.SendToAddressAsync(keyManager.GetNextReceiveKey("A").GetP2wpkhAddress(network), Money.Coins(1m));
		Assert.NotNull(txId);
		txId = await rpc.SendToAddressAsync(keyManager.GetNextReceiveKey("B").GetP2wpkhAddress(network), Money.Coins(0.5m));
		Assert.NotNull(txId);
		txId = await rpc.SendToAddressAsync(keyManager.GetNextReceiveKey("C").GetP2wpkhAddress(network), Money.Coins(0.25m));
		Assert.NotNull(txId);
		await rpc.GenerateAsync(1);

		try
		{
			Interlocked.Exchange(ref setup.FiltersProcessedByWalletCount, 0);
			nodes.Connect(); // Start connection service.
			node.VersionHandshake(); // Start mempool service.
			await synchronizer.StartAsync(CancellationToken.None); // Start wasabi synchronizer service.
			await feeProvider.StartAsync(CancellationToken.None);

			// Start wallet and filter processing service
			using var wallet = await walletManager.AddAndStartWalletAsync(keyManager);

			// Wait until the filter our previous transaction is present.
			var blockCount = await rpc.GetBlockCountAsync();
			await setup.WaitForFiltersToBeProcessedAsync(TimeSpan.FromSeconds(120), blockCount);
			wallet.Password = password;

			var waitCount = 0;
			while (wallet.Coins.Sum(x => x.Amount) == Money.Zero)
			{
				await Task.Delay(1000);
				waitCount++;
				if (waitCount >= 21)
				{
					throw new InvalidOperationException($"Funding transaction to the wallet '{wallet.WalletName}' did not arrive.");
				}
			}

			var destination = keyManager.GetNextReceiveKey("foo").GetAddress(network);
			var amount = Money.Coins(1.4m);
			var satPerBytesToTry = new List<decimal>
			{
				0,
				1,
				500,
				99999999
			};

			// Test for normal transactions
			foreach (var satPerByte in satPerBytesToTry)
			{
				var foundSolution = FeeHelpers.TryGetMaxFeeRate(wallet, destination, amount, "", new FeeRate(satPerByte), wallet.Coins, false, out var maxFeeRate);

				Assert.True(foundSolution);
				var failingFeeRate = new FeeRate(maxFeeRate!.SatoshiPerByte + 0.001m);
				try
				{
					wallet.BuildTransaction(destination, amount, "", failingFeeRate, wallet.Coins, false);
					Assert.Fail("Build should have failed due to high fee.");
				}
				catch (Exception ex) when (ex is NotEnoughFundsException or TransactionFeeOverpaymentException or InsufficientBalanceException || (ex is InvalidTxException itx && itx.Errors.OfType<FeeTooHighPolicyError>().Any()))
				{
					// Ignored. This is what we expect.
				}
				catch (Exception ex)
				{
					Assert.Fail($"Unexpected exception: {ex.GetType} - {ex.Message}");
				}
			}

			// Test for changeless transactions
			foreach (var satPerByte in satPerBytesToTry)
			{
				var foundSolution = FeeHelpers.TryGetMaxFeeRateForChangeless(wallet, destination, "", new FeeRate(satPerByte), wallet.Coins, out var maxFeeRate);

				Assert.True(foundSolution);
				var failingFeeRate = new FeeRate(maxFeeRate!.SatoshiPerByte + 0.001m);
				try
				{
					wallet.BuildChangelessTransaction(destination, "", failingFeeRate, wallet.Coins);
					Assert.Fail("Build should have failed due to high fee.");
				}
				catch (Exception ex) when (ex is NotEnoughFundsException or TransactionFeeOverpaymentException or InsufficientBalanceException || (ex is InvalidTxException itx && itx.Errors.OfType<FeeTooHighPolicyError>().Any()))
				{
					// Ignored. This is what we expect.
				}
				catch (Exception ex)
				{
					Assert.Fail($"Unexpected exception: {ex.GetType} - {ex.Message}");
				}
			}

			// Normal - No solution test
			{
				var singleCoinRegistry = new CoinsRegistry();
				singleCoinRegistry.TryAdd(wallet.Coins.First());
				amount = singleCoinRegistry.First().Amount;

				var foundSolution = FeeHelpers.TryGetMaxFeeRate(wallet, destination, amount, "", new FeeRate(2.5m), singleCoinRegistry, false, out _);
				Assert.False(foundSolution);
			}
		}
		finally
		{
			bitcoinStore.IndexStore.NewFilters -= setup.Wallet_NewFiltersProcessed;
			await walletManager.RemoveAndStopAllAsync(CancellationToken.None);
			await synchronizer.StopAsync(CancellationToken.None);
			await feeProvider.StopAsync(CancellationToken.None);
			nodes?.Dispose();
			node?.Disconnect();
		}
	}
}
