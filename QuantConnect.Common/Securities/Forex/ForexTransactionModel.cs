﻿/*
* QUANTCONNECT.COM:
* FOREX Transaction Model Class - Default Transaction Model for FX Data.
*/

/**********************************************************
* USING NAMESPACES
**********************************************************/
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using QuantConnect.Logging;

namespace QuantConnect.Securities {

    /******************************************************** 
    * QUANTCONNECT PROJECT LIBRARIES
    *********************************************************/
    using QuantConnect.Models;


    /******************************************************** 
    * CLASS DEFINITIONS
    *********************************************************/
    /// <summary>
    /// Forex Transaction Model Class: Specific transaction fill models for FOREX orders
    /// </summary>
    public class ForexTransactionModel : ISecurityTransactionModel {

        /******************************************************** 
        * CLASS PRIVATE VARIABLES
        *********************************************************/


        /******************************************************** 
        * CLASS PUBLIC VARIABLES
        *********************************************************/


        /******************************************************** 
        * CLASS CONSTRUCTOR
        *********************************************************/
        /// <summary>
        /// Initialise the Algorithm Transaction Class
        /// </summary>
        public ForexTransactionModel() {

        }

        /******************************************************** 
        * CLASS PROPERTIES
        *********************************************************/


        /******************************************************** 
        * CLASS METHODS
        *********************************************************/
        /// <summary>
        /// Perform neccessary check to see if the model has been filled, appoximate the best we can.
        /// </summary>
        /// <param name="vehicle">Asset we're working with</param>
        /// <param name="order">Order class to check if filled.</param>
        public virtual OrderEvent Fill(Security vehicle, Order order) 
        {    
            var fill = new OrderEvent(order);

            try {
                switch (order.Type) {
                    case OrderType.Limit:
                        fill = LimitFill(vehicle, order);
                        break;
                    case OrderType.StopMarket:
                        fill = StopFill(vehicle, order);
                        break;
                    case OrderType.Market:
                        fill = MarketFill(vehicle, order);
                        break;
                }
            } catch (Exception err) {
                Log.Error("ForexTransactionModel.TransOrderDirection.Fill(): " + err.Message);
            }

            return fill;
        }



        /// <summary>
        /// Get the Slippage approximation for this order:
        /// </summary>
        public virtual decimal GetSlippageApproximation(Security security, Order order)
        {
            //Return 0 by default
            decimal slippage = 0;
            //For FOREX, the slippage is the Bid/Ask Spread for Tick, and an approximation for the 
            switch (security.Resolution)
            {
                case Resolution.Minute:
                case Resolution.Second:
                    //Get the last data packet:
                    TradeBar lastBar = (TradeBar) security.GetLastData();
                    //Assume slippage is 1/10,000th of the price
                    slippage = lastBar.Value*0.0001m;
                    break;

                case Resolution.Tick:
                    Tick lastTick = (Tick) security.GetLastData();
                    switch (order.Direction)
                    {
                        case OrderDirection.Buy:
                            //We're buying, assume slip to Asking Price.
                            slippage = Math.Abs(order.Price - lastTick.AskPrice);
                            break;

                        case OrderDirection.Sell:
                            //We're selling, assume slip to the bid price.
                            slippage = Math.Abs(order.Price - lastTick.BidPrice);
                            break;
                    }
                    break;
            }
            return slippage;
        }



        /// <summary>
        /// Model the slippage on a market order: fixed percentage of order price
        /// </summary>
        /// <param name="security">Asset we're working with</param>
        /// <param name="order">Order to update</param>
        public virtual OrderEvent MarketFill(Security security, Order order)
        {
            var fill = new OrderEvent(order);
            try 
            {
                //Calculate the model slippage: e.g. 0.01c
                decimal slip = GetSlippageApproximation(security, order);

                switch (order.Direction)
                {
                    case OrderDirection.Buy:
                        //Set the order and slippage on the order, update the fill price:
                        order.Price = security.Price;
                        order.Price += slip;
                        break;

                    case OrderDirection.Sell:
                        //Set the order and slippage on the order, update the fill price:
                        order.Price = security.Price;
                        order.Price -= slip;
                        break;
                }

                //Market orders fill instantly.
                order.Status = OrderStatus.Filled;

                //Assume 100% fill for market & modelled orders.
                fill.FillQuantity = order.Quantity;
                fill.FillPrice = order.Price;
                fill.Status = order.Status;
            }
            catch (Exception err) 
            {
                Log.Error("ForexTransactionModel.TransOrderDirection.MarketFill(): " + err.Message);
            }
            return fill;
        }




        /// <summary>
        /// Check if the model has stopped out our position yet:
        /// </summary>
        /// <param name="security">Asset we're working with</param>
        /// <param name="order">Stop Order to Check, return filled if true</param>
        public virtual OrderEvent StopFill(Security security, Order order)
        {
            var fill = new OrderEvent(order);
            try 
            {
                //If its cancelled don't need anymore checks:
                if (order.Status == OrderStatus.Canceled) return fill;

                //Check if the Stop Order was filled: opposite to a limit order
                if (order.Direction == OrderDirection.Sell) 
                {
                    //-> 1.1 Sell Stop: If Price below setpoint, Sell:
                    if (security.Price < order.Price) 
                    {
                        //Set the order and slippage on the order, update the fill price:
                        order.Status = OrderStatus.Filled;
                        order.Price = security.Price;   //Fill at the security price, sometimes gap down skip past stop.
                    }
                } 
                else if (order.Direction == OrderDirection.Buy) 
                {
                    //-> 1.2 Buy Stop: If Price Above Setpoint, Buy:
                    if (security.Price > order.Price) 
                    {
                        order.Status = OrderStatus.Filled;
                        order.Price = security.Price;   //Fill at the security price, sometimes gap down skip past stop.
                    }
                }

                //Set the fill properties when order filled.
                if (order.Status == OrderStatus.Filled || order.Status == OrderStatus.PartiallyFilled)
                {
                    fill.FillQuantity = order.Quantity;
                    fill.FillPrice = security.Price;
                    fill.Status = order.Status;
                }
            } 
            catch (Exception err) 
            {
                Log.Error("ForexTransactionModel.TransOrderDirection.StopFill(): " + err.Message);
            }
            return fill;
        }



        /// <summary>
        /// Check if the price MarketDataed to our limit price yet:
        /// </summary>
        /// <param name="security">Asset we're working with</param>
        /// <param name="order">Limit order in market</param>
        public virtual OrderEvent LimitFill(Security security, Order order)
        {

            //Initialise;
            decimal marketDataMinPrice = 0;
            decimal marketDataMaxPrice = 0;
            var fill = new OrderEvent(order);

            try 
            {
                //If its cancelled don't need anymore checks:
                if (order.Status == OrderStatus.Canceled) return fill;
                //Depending on the resolution, return different data types:
                BaseData marketData = security.GetLastData();

                if (marketData.DataType == MarketDataType.TradeBar)
                {
                    marketDataMinPrice = ((TradeBar)marketData).Low;
                    marketDataMaxPrice = ((TradeBar)marketData).High;
                } 
                else 
                {
                    marketDataMinPrice = marketData.Value;
                    marketDataMaxPrice = marketData.Value;
                }

                //-> Valid Live/Model Order: 
                if (order.Direction == OrderDirection.Buy) 
                {
                    //Buy limit seeks lowest price
                    if (marketDataMinPrice < order.Price) 
                    {
                        order.Status = OrderStatus.Filled;
                    }

                } 
                else if (order.Direction == OrderDirection.Sell) 
                {
                    //Sell limit seeks highest price possible
                    if (marketDataMaxPrice > order.Price) 
                    {
                        order.Status = OrderStatus.Filled;
                    }
                }

                //Fill price
                if (order.Status == OrderStatus.Filled || order.Status == OrderStatus.PartiallyFilled)
                {
                    fill.FillQuantity = order.Quantity;
                    fill.FillPrice = order.Price;
                    fill.Status = order.Status;
                }
            } 
            catch (Exception err)
            {
                Log.Error("ForexTransactionModel.TransOrderDirection.LimitFill(): " + err.Message);
            }
            return fill;
        }



        /// <summary>
        /// Get the fees from one order, interactive brokers model.
        /// </summary>
        /// <param name="quantity"></param>
        /// <param name="price"></param>
        public virtual decimal GetOrderFee(decimal quantity, decimal price)
        {
            //Modelled order fee to 0; Assume spread is the fee for most FX brokerages.
            return 0;
        }

    } // End Algorithm Transaction Filling Classes

} // End QC Namespace
