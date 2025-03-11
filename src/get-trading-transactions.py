import json
from collections import defaultdict
import os
import json
import pandas as pd
import numpy as np
import matplotlib.pyplot as plt
import statsmodels.api as sm
from datetime import datetime
import argparse
from yachalk import chalk
import json
import requests
import pandas as pd
from datetime import datetime, timedelta
from dotenv import load_dotenv

#
# global
#

load_dotenv()
FMP_API_KEY = os.getenv("FINANCIAL_MODELING_PREP_KEY")

number_of_required_stocks_per_snapshot = 400 # TODO: adjust when number of stocks per snapshot are smaller!
number_of_top_stocks = 40

#
#
#

def main(snapshot_file):

    # Load the JSON data
    with open(snapshot_file, 'r') as f:
        snapshots = json.load(f)

    # Initialize variables
    portfolio = {}
    transactions = []
    portfolio_values = []
    total_value = 100000
    available_cash = 100000
    price_cache = {}  # Cache for stock prices

    def get_cached_price(symbol, date):
        cache_key = f"{symbol}_{date}"
        if cache_key not in price_cache:
            price_cache[cache_key] = get_stock_price(symbol, date)
        return price_cache[cache_key]

    print(f"TYPE\tDATE\tTICKER\tQUANTITY\tPRICE\tAMOUNT")

    # Process each snapshot
    for date, stocks in sorted(snapshots.items()):

        if len(stocks) < number_of_required_stocks_per_snapshot:
            continue

        # Select top stocks, filtering out None scores
        valid_stocks = [stock for stock in stocks if stock['score'] is not None]
        top_stocks = sorted(valid_stocks, key=lambda x: x['score'], reverse=True)[:number_of_top_stocks]
        top_stock_symbols = set(stock['symbol'] for stock in top_stocks)
        
        # Sell stocks no longer in top
        for symbol in list(portfolio.keys()):
            if symbol not in top_stock_symbols:
                price = get_cached_price(symbol, date)
                if price:
                    quantity = portfolio[symbol]
                    sell_amount = quantity * price
                    transactions.append({
                        'Date': date,
                        'Action': 'Sell',
                        'Symbol': symbol,
                        'Quantity': quantity,
                        'Price': price,
                        'Amount': sell_amount
                    })
                    available_cash += sell_amount
                    del portfolio[symbol]
                    print(f"SELL\t{date}\t{stock['symbol']}\t{quantity}\t{price:.2f}\t{sell_amount:.2f}")


        # Buy new stocks
        new_stocks = [stock for stock in top_stocks if stock['symbol'] not in portfolio]
        if new_stocks:
            # Distribute cash evenly, but only buy whole shares
            remaining_stocks = len(new_stocks)
            for stock in new_stocks:
                amount_per_stock = available_cash / remaining_stocks
                price = get_cached_price(stock['symbol'], date)
                if price:
                    quantity = int(amount_per_stock / price)  # Only whole shares
                    if quantity > 0:
                        buy_amount = quantity * price
                        transactions.append({
                            'Date': date,
                            'Action': 'Buy',
                            'Symbol': stock['symbol'],
                            'Quantity': quantity,
                            'Price': price,
                            'Amount': buy_amount
                        })
                        portfolio[stock['symbol']] = quantity
                        available_cash -= buy_amount
                        print(f"BUY\t{date}\t{stock['symbol']}\t{quantity}\t{price:.2f}\t{buy_amount:.2f}")
                    remaining_stocks -= 1

        # Calculate and track portfolio value
        portfolio_value = sum(get_cached_price(symbol, date) * quantity for symbol, quantity in portfolio.items() if get_cached_price(symbol, date))
        total_value = portfolio_value + available_cash
        portfolio_values.append({
            'Date': date,
            'Portfolio Value': portfolio_value,
            'Cash': available_cash,
            'Total Value': total_value
        })

    # Sell all remaining stocks after last snapshot
    last_date = max(snapshots.keys())
    print(f"\nFinal sell-off on {last_date}:")
    for symbol, quantity in list(portfolio.items()):
        price = get_cached_price(symbol, last_date)
        if price:
            sell_amount = quantity * price
            transactions.append({
                'Date': last_date,
                'Action': 'Sell',
                'Symbol': symbol,
                'Quantity': quantity,
                'Price': price,
                'Amount': sell_amount
            })
            available_cash += sell_amount
            del portfolio[symbol]
            print(f"SELL\t{last_date}\t{symbol}\t{quantity}\t{price:.2f}\t{sell_amount:.2f}")

    print(f"\nFinal cash position: {available_cash:.2f}")

    # Create and export DataFrames
    df = pd.DataFrame(transactions)
    df.to_excel('analyst-trading-transactions.xlsx', index=False)
    print("Trading Transactions exported to portfolio-values.xlsx")
   
    df_portfolio = pd.DataFrame(portfolio_values)
    df_portfolio.to_excel('analyst-portfolio-values.xlsx', index=False)
    print("Portfolio values exported to portfolio-values.xlsx")

    return 0

def get_stock_price(symbol, date):
        url = f'https://financialmodelingprep.com/api/v3/historical-price-full/{symbol}?from={date}&to={date}&apikey={FMP_API_KEY}'
        response = requests.get(url)
        data = response.json()
        if 'historical' in data and len(data['historical']) > 0:
            return data['historical'][0]['close']
        return None

#
# main
#

if __name__ == "__main__":
    # Setup arguments using argparse
    parser = argparse.ArgumentParser(description="Generate Trading Transactions and Portfolio Values")
    parser.add_argument('--snapshot-file', required=True, help="Path to the snapshot file")

    # Parse arguments
    args = parser.parse_args()

    # Execute main function with parsed arguments
    ret = main(snapshot_file=args.snapshot_file)
    if ret != 0:
        print(chalk.red(f"ERROR: {__file__} failed"))
        exit(ret)
