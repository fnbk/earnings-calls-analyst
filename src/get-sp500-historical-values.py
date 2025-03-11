import json
import pandas as pd
import argparse
from yachalk import chalk
import requests
from datetime import datetime
from dotenv import load_dotenv
import os

#
# global
#

load_dotenv()
FMP_API_KEY = os.getenv("FINANCIAL_MODELING_PREP_KEY")

number_of_required_stocks_per_snapshot = 400 # TODO: adjust when number of stocks per snapshot are smaller!

#
#
#

def main(snapshot_file):
    # Load snapshots
    with open(snapshot_file, 'r') as f:
        snapshots = json.load(f)

    # Get valid snapshot dates
    valid_dates = sorted([
        date for date, stocks in snapshots.items() 
        if len(stocks) >= number_of_required_stocks_per_snapshot
    ])

    if not valid_dates:
        print("No valid dates found in snapshot file")
        return 1

    # Get all prices in one request
    print("Fetching S&P 500 historical prices...")
    start_date = valid_dates[0]
    end_date = valid_dates[-1]
    prices = get_sp500_prices(start_date, end_date)

    # Create data for valid snapshot dates
    data = []
    last_known_price = None
    
    for date in valid_dates:
        current_price = prices.get(date)
        
        if current_price is not None:
            last_known_price = current_price
        elif last_known_price is not None:
            print(f"Using last known price for {date}")
            current_price = last_known_price
        else:
            print(f"Warning: No price found for {date} and no previous price available")
            continue

        data.append({
            'date': date,
            'S&P 500': current_price
        })
        print(f"Processed {date}: S&P 500 = {current_price:,.2f}")

    # Create DataFrame and format
    df = pd.DataFrame(data)
    
    # Export to Excel with formatting
    df.to_excel(
        'sp500-historical-values.xlsx',
        float_format='%.2f'
    )

    print(chalk.green(f"\nExported to sp500-historical-values.xlsx"))
    print(f"Processed {len(data)} dates from {start_date} to {end_date}")
    return 0

def get_sp500_prices(start_date, end_date):
    url = f'https://financialmodelingprep.com/api/v3/historical-price-full/^GSPC?from={start_date}&to={end_date}&apikey={FMP_API_KEY}'
    response = requests.get(url)
    data = response.json()
    
    if 'historical' not in data:
        return {}
    
    # Create a dictionary with date as key and price as value
    return {
        item['date']: item['close'] 
        for item in data['historical']
    }

#
# main
#

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Get S&P 500 Historical Values")
    parser.add_argument('--snapshot-file', required=True, help="Path to the snapshot file")

    args = parser.parse_args()
    
    try:
        ret = main(snapshot_file=args.snapshot_file)
        if ret != 0:
            print(chalk.red(f"ERROR: {__file__} failed"))
            exit(ret)
    except Exception as e:
        print(chalk.red(f"ERROR: {str(e)}"))
        exit(1) 