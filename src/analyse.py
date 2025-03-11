import os
import json
import pandas as pd
import numpy as np
import matplotlib.pyplot as plt
import statsmodels.api as sm
from datetime import datetime
import argparse
from yachalk import chalk

def main(snapshot_file):

    #
    # 1. Load Data
    #

    # Read snapshots.json
    snapshot_dates = 0
    first_date = ""
    last_date = ""
    number_of_required_stocks_per_snapshot = 400 # TODO: adjust when number of stocks per snapshot are smaller!

    data = []
    with open(snapshot_file, 'r') as f:
        snapshots = json.load(f)
        for date in snapshots.keys():
            # Only include snapshots with more than 400 entries
            if len(snapshots[date]) >= number_of_required_stocks_per_snapshot:
                if first_date == "":
                    first_date = date
                last_date = date
                snapshot_dates += 1
                for earnings_call in snapshots[date]:
                    earnings_call["snapshot"] = date
                    data.append(earnings_call)

    print(f"number_of_required_stocks_per_snapshot: {number_of_required_stocks_per_snapshot} snapshot_dates: {snapshot_dates}, first_date: {first_date} last_date: {last_date}")
    
    #
    # 2. Add Data
    #

    # Create data container
    df = pd.DataFrame(data)

    # Sort by date
    df['snapshot'] = pd.to_datetime(df['snapshot'])
    df = df.sort_values(by='snapshot')
    
    # calculate returns
    df['return_day2'] = df.apply(lambda row: calculate_return(row, 'day1_price', 'day2_price'), axis=1)
    df['return_day7'] = df.apply(lambda row: calculate_return(row, 'day1_price', 'day7_price'), axis=1)
    df['return_day30'] = df.apply(lambda row: calculate_return(row, 'day1_price', 'day30_price'), axis=1)

    df['return_sp500_day2'] = df.apply(lambda row: calculate_return(row, 'day1_sp500_price', 'day2_sp500_price'), axis=1)
    df['return_sp500_day7'] = df.apply(lambda row: calculate_return(row, 'day1_sp500_price', 'day7_sp500_price'), axis=1)
    df['return_sp500_day30'] = df.apply(lambda row: calculate_return(row, 'day1_sp500_price', 'day30_sp500_price'), axis=1)

    df['excess_day2'] = df.apply(lambda row: calculate_excess_return(row, 'day1_price', 'day2_price', 'day1_sp500_price', 'day2_sp500_price'), axis=1)
    df['excess_day7'] = df.apply(lambda row: calculate_excess_return(row, 'day1_price', 'day7_price', 'day1_sp500_price', 'day7_sp500_price'), axis=1)
    df['excess_day30'] = df.apply(lambda row: calculate_excess_return(row, 'day1_price', 'day30_price', 'day1_sp500_price', 'day30_sp500_price'), axis=1)

    #
    # 3. Quantile Analysis
    #

    df['quantile'] = pd.qcut(df["score"], q=10, labels=False, duplicates='drop') # Divide into 10 quantiles

    #
    # 3a. normal returns
    #

    analysis = df.groupby('quantile')[['return_day2', 'return_day7', 'return_day30']].mean()
    perform_quantile_analysis(analysis, "quantile-analysis-normal-returns.png")

    #
    # 3b. excess returns
    #
    
    analysis = df.groupby('quantile')[['excess_day2', 'excess_day7', 'excess_day30']].mean()
    perform_quantile_analysis(analysis, "quantile-analysis-excess-returns.png")
   
    #
    # 3c. 30 day returns
    #
    
    analysis = df.groupby('quantile')[['return_sp500_day30', 'return_day30', 'excess_day30']].mean()
    perform_quantile_analysis(analysis, "quantile-analysis-30day-returns.png")

    #
    # 3d. 30 day excess retuns
    #
    
    analysis = df.groupby('quantile')[['excess_day30']].mean()
    perform_quantile_analysis(analysis, "quantile-analysis-30day-excess-returns.png")


    #
    # 4. Regression Analysis
    #

    # Define independent and dependent variables
    X = df["score"] # Independent variable (Composite Score)
    y = df['excess_day30']
    # y = df['return_day30']

    # Add a constant to the independent variable (for intercept)
    X = sm.add_constant(X)

    # Perform OLS regression
    model = sm.OLS(y, X, missing='drop').fit() # Drop rows with NaN

    # Print regression results
    print("\nRegression Analysis:")
    summary = model.summary()
    summary.extra_txt = '' # Remove Notes section
    print(summary)

    return 0

def calculate_return(row, base_col, price_col):
    if row[base_col] != 0:
        return (row[price_col] - row[base_col]) / row[base_col]
    else:
        return np.nan

def calculate_excess_return(row, ticker_base_col, ticker_price_col, sp500_base_col, sp500_price_col):
    if row[ticker_base_col] != 0 and row[ticker_price_col] != 0 and row[sp500_base_col] != 0 and row[sp500_price_col] != 0:
        stock_return = (row[ticker_price_col] - row[ticker_base_col]) / row[ticker_base_col]
        sp500_return = (row[sp500_price_col] - row[sp500_base_col]) / row[sp500_base_col]
        return stock_return - sp500_return
    else:
        return np.nan
        
def perform_quantile_analysis(analysis, filename):
    
    print(f"\n{filename}:")
    print(analysis)

    # Visualize Quantile Analysis (Bar Chart)
    analysis.plot(kind='bar', figsize=(12, 6))
    # analysis.plot(kind='bar', figsize=(12, 6), color='green')
    plt.title('Average Returns by Composite Score Quantile')
    plt.xlabel('Composite Score Quantile')
    plt.ylabel('Average Return')
    plt.xticks(rotation=45) # Rotate x-axis labels for readability
    plt.tight_layout() # Adjust layout to prevent labels from overlapping

    # Save the plot to file
    script_dir = os.path.dirname(os.path.abspath(__file__))
    chart_file = os.path.join(script_dir, filename)
    plt.savefig(chart_file)
    plt.close()

    # Print where chart file has been stored
    print(chalk.green("VISUAL CHART created at:"), chart_file, "\n")

#
# main
#

if __name__ == "__main__":
    # Setup arguments using argparse
    parser = argparse.ArgumentParser(description="Analyse Snapshots")
    parser.add_argument('--snapshot-file', required=True, help="Path to the snapshot file")

    # Parse arguments
    args = parser.parse_args()

    # Execute main function with parsed arguments
    ret = main(snapshot_file=args.snapshot_file)
    if ret != 0:
        print(chalk.red(f"ERROR: {__file__} failed"))
        exit(ret)

