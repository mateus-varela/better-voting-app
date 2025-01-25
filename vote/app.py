import os
import json
import logging
import socket
import random

from flask import Flask, render_template, request, make_response, g

from kafka import KafkaProducer
from kafka.admin import KafkaAdminClient, NewTopic
from kafka.errors import NoBrokersAvailable

from prometheus_client import Counter, generate_latest, Histogram, REGISTRY

option_a = os.getenv('OPTION_A', "Cats")
option_b = os.getenv('OPTION_B', "Dogs")
hostname = socket.gethostname()
version = os.getenv('VERSION', 'V1')

app = Flask(__name__)

gunicorn_error_logger = logging.getLogger('gunicorn.error')
app.logger.handlers.extend(gunicorn_error_logger.handlers)
app.logger.setLevel(logging.INFO)

kafka_brokers_list = os.getenv('KAFKA_BROKERS_LIST')
kafka_username = os.getenv('KAFKA_USERNAME')
kafka_password = os.getenv('KAFKA_PASSWORD')

votes_counter = Counter('votes_total', 'Total number of votes')
option_a_votes = Counter('option_a_votes_total', 'Total number of votes for Option A')
option_b_votes = Counter('option_b_votes_total', 'Total number of votes for Option B')
vote_processing_time = Histogram('vote_processing_seconds', 'Time taken to process a vote')

def get_kafka_admin_client():
    try:
        admin_client = KafkaAdminClient(
            bootstrap_servers=kafka_brokers_list.split(','),
            security_protocol='SASL_PLAINTEXT',
            sasl_mechanism='PLAIN',
            sasl_plain_username=kafka_username,
            sasl_plain_password=kafka_password,
        )
        app.logger.info('KafkaAdminClient created successfully')
        return admin_client
    except Exception as e:
        app.logger.error(f'Error creating KafkaAdminClient: {e}')
        return None

def create_kafka_topic(topic_name):
    try:
        admin_client = get_kafka_admin_client()
        if admin_client is None:
            app.logger.error('Kafka admin client is None')
            return
        topic_metadata = admin_client.list_topics()
        if topic_name not in topic_metadata:
            new_topic = NewTopic(name=topic_name, num_partitions=1, replication_factor=3)
            admin_client.create_topics(new_topics=[new_topic])
            app.logger.info(f"Created Kafka topic: {topic_name}")
        else:
            app.logger.info(f"Kafka topic '{topic_name}' already exists")
    except Exception as e:
        app.logger.error(f'Error creating Kafka topic: {e}')

def get_kafka_producer():
    try:
        create_kafka_topic("voting-app-topic")
        producer = KafkaProducer(
            bootstrap_servers=kafka_brokers_list.split(','),
            security_protocol='SASL_PLAINTEXT',
            sasl_mechanism='PLAIN',
            sasl_plain_username=kafka_username,
            sasl_plain_password=kafka_password,
            value_serializer=lambda v: json.dumps(v).encode('utf-8'),
            api_version=(2, 7, 0) 
        )
        app.logger.info('KafkaProducer created successfully')
        return producer
    except NoBrokersAvailable as e:
        app.logger.error(f'No brokers available: {e}')
        return None
    except Exception as e:
        app.logger.error(f'Error creating KafkaProducer: {e}')
        return None

@app.route("/metrics")
def metrics():
    return generate_latest(REGISTRY)

@app.route("/", methods=['POST', 'GET'])
def hello():
    try:
        voter_id = request.cookies.get('voter_id')
        if not voter_id:
            voter_id = hex(random.getrandbits(64))[2:-1]

        vote = None

        if request.method == 'POST':
            producer = get_kafka_producer()
            if producer is None:
                return "Kafka broker not available", 500

            vote = request.form['vote']
            app.logger.info('Received vote for %s', vote)
            data = {'voter_id': voter_id, 'vote': vote}
            
            try:
                with vote_processing_time.time():
                    producer.send('voting-app-topic', value=data)
                    producer.flush()
                app.logger.info('Message sent to Kafka topic successfully')
                votes_counter.inc()
                if vote == 'a':
                    option_a_votes.inc()
                elif vote == 'b':
                    option_b_votes.inc()
            except Exception as e:
                app.logger.error(f'Error sending message to Kafka: {e}')
                return "Error sending vote", 500

        resp = make_response(render_template(
            'index.html',
            option_a=option_a,
            option_b=option_b,
            hostname=hostname,
            vote=vote,
            version=version
        ))
        resp.set_cookie('voter_id', voter_id)
        return resp
    except Exception as e:
        app.logger.error(f"Error in hello route: {e}")
        return "Internal Server Error", 500

if __name__ == "__main__":
    app.run(host='0.0.0.0', port=80, debug=True, threaded=True)
